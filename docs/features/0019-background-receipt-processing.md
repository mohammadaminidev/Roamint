# 0019 — Background receipt processing with a live notification bar

## Context

Receipt capture is synchronous today (0015 decision, reaffirmed in 0018): `Capture →
IReceiptsApi.UploadAsync` blocks the Blazor circuit 3–8 s behind a spinner while Gemini
runs OCR, the host then resolves each line against the learned item vocabulary (0016),
and only then navigates to `/add`. The user stares at a spinner and can do nothing else
until it lands.

This feature **decouples capture from review**. After a shot, the upload returns near-
instantly; OCR + matching run in the background; a **persistent notification bar** (pinned
in the phone shell, surviving navigation) shows each receipt's stage and, when a receipt
is ready, offers a **Review** action that opens the pre-filled add-expense form. The user
can keep snapping in the meantime — **batch capture**: photograph several receipts, review
each when convenient.

0015 called synchronous "correct for one user; revisit only if it annoys." It annoys, and
the win is more than hiding 5 seconds — it's *snap-and-walk-away* plus rapid multi-receipt
capture. That is the justification for the added machinery below.

### What this is *not*

- **Not** a new bounded context. The Receipts module gains one use-case split (below);
  the queue, worker, job tracker, and bar are all **web-host** concerns, exactly where
  the OCR-then-match orchestration already lives today (the page does it inline). Module
  boundaries are unchanged — no module calls another, no integration event.
- **Not** the outbox drain. Still write-only (0015). Background processing here is host
  orchestration over the existing public APIs, not an integration-event consumer.
- **Not** polling. Blazor Server already holds a live WebSocket circuit; the bar is
  **pushed** to via an in-process event. The "no status polling" argument 0015 used to
  justify synchronous does not apply to a push.
- **Not** parallel OCR. The worker processes the queue **serially** (FIFO), which also
  respects Gemini's free-tier rate limits — multiple in-flight receipts queue, one hits
  Gemini at a time.
- **Not** a real percentage. Gemini is one opaque call; progress is honest **discrete
  stages** with an indeterminate animation, never a fabricated %.

## Decisions

- **Split `UploadReceipt` into upload + parse.** Today `UploadReceipt` does store-image +
  OCR + save in one transaction. Split into:
  - `UploadReceipt(image, contentType)` → stores the image, creates `Receipt(Uploaded)`,
    saves, returns the id. **Fast** — one DB write, no external call.
  - `ParseReceipt(receiptId)` → loads the receipt, runs OCR, `receipt.Parse(draft)` or
    `MarkFailed`, saves. **This is the backgrounded step.**

  This makes `Uploaded` a real *persisted resting state* rather than a transient inside a
  single transaction. The existing `Receipt` state machine already enforces exactly this:
  `Parse`/`MarkFailed` are valid only from `Uploaded`. **No new statuses** — in particular
  no `Parsing` state (0015 deliberately avoided a parse race); "in progress" is UI state on
  the job tracker, the persisted receipt stays `Uploaded` until `Parsed`/`Failed`. Crash
  recovery is handled by a startup re-enqueue sweep (below), not a DB flag.

- **The worker re-reads the image from the store**, it does not carry bytes through the
  queue. `ParseReceipt` opens the stored image by key, so a queued job survives the brief
  upload→parse gap and a future retry can re-run OCR from the same image. Needs a small
  `IReceiptImageStore.OpenAsync(key)` read method (today the store is write-only). This
  also unlocks the deferred *re-scan a Failed receipt* path (0015 open question).

- **Host background pipeline = a singleton tracker + a hosted worker.**
  - `ReceiptJobs` (singleton) — the source of truth for in-flight and recently-finished
    jobs. Wraps a bounded `Channel<Guid>` (the receipt id is the job key) and a bounded
    list of job states; exposes `Enqueue`, `Snapshot`, and a `Changed` event. Singleton so
    the worker and every live circuit (phone tab, laptop tab) observe one shared queue —
    correct for a single user.
  - `ReceiptProcessingWorker : BackgroundService` — dequeues, opens a DI scope per job,
    calls `IReceiptsApi.ParseAsync` then the 0016 match loop, advancing the job's stage via
    `ReceiptJobs` at each step. On success it stashes the fully-seeded `ReceiptSeed` on the
    job; on failure, the reason.

- **Matching stays in the host.** The worker calls `IExpensesQueries.MatchItemAsync` —
  the same cross-module read the page does today, allowed as host orchestration. Producing
  the seed in the worker means "Review" is instant: the job already holds the matched seed.

- **Push to the bar over the live circuit.** `ReceiptJobBar` subscribes to
  `ReceiptJobs.Changed` and marshals updates with `InvokeAsync(StateHasChanged)`;
  unsubscribes on dispose. No polling, no new endpoint, no SignalR hub beyond the circuit
  already in place.

- **Tap-to-review, not auto-navigate.** A `Ready` job shows a Review button; the user taps
  when ready, which sets `ReceiptHandoff` from the job's stashed seed and navigates to
  `/add`. This preserves batch capture (auto-jump would interrupt the next shot). Same
  `ReceiptHandoff` plumbing as today — only the *timing* of population moves (review-tap
  instead of upload).

- **Startup re-enqueue sweep** (Development, mirrors the migrate-on-startup services). On
  boot, any receipt left `Uploaded` (orphaned by a crash mid-parse) is re-enqueued. Cheap,
  and it doubles as the recovery path; a user-facing retry button is deferred.

- **Honest stage progress.** Stages: `Queued → Reading items → Matching items →
  Ready` (or `Failed`). The bar animates an indeterminate indicator during a stage; the
  `Ready` row shows merchant + total + item count pulled from the seed.

## Domain model (Receipts module)

No new aggregate, value object, or status. The change is the use-case split:

- **`UploadReceipt`** (modified) — stores image, `Receipt.Upload(...)`, save, return id.
  Drops the inline OCR call.
- **`ParseReceipt`** (new slice) — load `Receipt` by id, `OpenAsync` the image, run
  `IReceiptOcr.ExtractAsync`, `receipt.Parse(draft)` on success / `receipt.MarkFailed` on
  OCR failure (same best-effort posture: a thrown adapter fails the receipt, not the
  request), save, return the draft.

`Receipt` state machine (`Uploaded → Parsed → Linked`, plus `Failed`) is unchanged and
already correct for the split. Three+ use cases now (`Upload`, `Parse`, `Link`, `Get`) —
module still **flat**, no subfolders.

## Public surface (`Roamint.Receipts.Contracts`)

- `IReceiptsApi`:
  - `Task<Guid> UploadAsync(UploadReceiptRequest, CancellationToken)` — **changed** return:
    just the new receipt id (no draft; OCR hasn't run yet).
  - `Task<ParseReceiptResult> ParseAsync(Guid receiptId, CancellationToken)` — **new**;
    runs OCR, returns `(bool Parsed, ParsedDraftDto? Draft)`. Called by the host worker.
  - `Task LinkAsync(Guid receiptId, Guid expenseId, CancellationToken)` — unchanged.
- `IReceiptsQueries.GetAsync` — unchanged (status + draft, used by the sweep / direct nav).
- DTOs: `UploadReceiptResult` is replaced by the bare id; add `ParseReceiptResult(bool
  Parsed, ParsedDraftDto? Draft)`. `ParsedDraftDto` / `ParsedLineDto` unchanged.

Host-only types (no contract surface): `ReceiptJobs`, `ReceiptJob` (id, stage, seed,
error, timestamps), `ReceiptProcessingWorker`, `ReceiptJobBar.razor`. `ReceiptHandoff`
and `ReceiptSeed` unchanged.

## Web UI (`Roamint.Web`)

- `ReceiptCapture.razor`: `Capture` / `OnFile` → `UploadAsync` (fast) →
  `ReceiptJobs.Enqueue(id)` → clear `busy`, ready for the next shot. The OCR spinner and
  the inline match loop are removed from this page (moved to the worker).
- `ReceiptJobBar.razor`: new component in the `.phone` shell (so it persists across nav).
  Renders one row per active/finished job — stage label + indeterminate bar; `Ready` rows
  show merchant · total · item count and a **Review** button (sets `ReceiptHandoff` from
  the job seed, navigates to `/add`). Dismissable rows; subscribes/unsubscribes to
  `ReceiptJobs.Changed`.
- `ReceiptJobBar.razor.css`: the slim stacked-row styling (see the 0019 mockup).

## Wiring

- `ReceiptsModule.AddReceiptsModule` registers the new `ParseReceipt` handler (already
  covered by the Application assembly scan) and the `IReceiptImageStore.OpenAsync` impl.
- `Roamint.Web/Program.cs` registers `ReceiptJobs` (singleton),
  `ReceiptProcessingWorker` (`AddHostedService`), and the startup re-enqueue sweep.

## Open questions

- **Job-state retention / dismissal.** How long does a `Ready` job linger before auto-
  clearing, and does dismissing it without reviewing leave the receipt `Parsed`-but-
  unlinked forever? Probably fine (it just never becomes an expense), but worth a glance at
  whether an "unreviewed receipts" list is wanted later.
- **User-facing retry** for `Failed` receipts — the `OpenAsync` + `ParseReceipt` split makes
  it trivial to re-enqueue; a button is deferred until failures actually happen.
- **Multi-circuit nuance.** Singleton `ReceiptJobs` means a job started on the phone is also
  visible on a laptop tab. Desirable for one user; noted in case it surprises.
- **Bar placement vs. the existing `NavBar`.** The mockup pins the stack just above the nav;
  confirm it doesn't crowd the small phone viewport when 2–3 jobs are active (collapse to a
  single summary row past N?).

## Verification

- **Unit (Domain):** `ParseReceipt`-from-non-`Uploaded` rejected; `MarkFailed` only from
  `Uploaded`; the split leaves `Upload` returning an `Uploaded` receipt with no draft.
- **Application:** `UploadReceipt` no longer calls OCR (fake `IReceiptOcr` not invoked).
  `ParseReceipt` with a fake `IReceiptOcr` returning a known draft → receipt `Parsed` with
  it; OCR throwing → `Failed`. `OpenAsync` round-trips the stored bytes.
- **Host pipeline:** enqueue two receipts → worker processes serially, job stages advance
  `Queued → Reading → Matching → Ready`, each `Ready` job carries a correctly-matched seed;
  a forced OCR failure surfaces as a `Failed` job, not a crash.
- **Startup sweep:** seed an `Uploaded` receipt, boot → it gets enqueued and parsed.
- **End-to-end (manual, phone over Tailscale/hotspot):** snap three receipts in a row →
  capture returns immediately each time → bar shows three rows progressing → tap Review on
  the first ready one → `/add` pre-filled (0016 matches intact) → confirm → expense recorded
  and receipt `Linked`; the other two remain reviewable in the bar.
