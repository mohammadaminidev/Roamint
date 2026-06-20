# 0015 — Receipts: photo OCR → pre-filled expense

## Context

First V2 feature. Replaces the slowest part of daily capture: typing line items by
hand. The user photographs a paper/thermal receipt with their phone; the app extracts
merchant, date, currency, and line items via an LLM vision call, then drops the result
into the existing add-expense review screen where the user assigns categories and
confirms. The confirmed expense is recorded through the **existing** Expenses write
API; the receipt is linked to it.

This is the **Receipts** bounded context coming online (schema `receipts.`), previously
a placeholder. It is a full vertical slice: Domain → Application → Infrastructure →
Contracts → web UI, with a real Gemini vision call behind the OCR port.

### What this is *not*

- **Not** an outbox-drain feature. An earlier framing assumed Receipts would be the
  first integration-event consumer. It is not: the receipt→expense link is orchestrated
  by the **web host**, which already holds both `IExpensesApi` and the new
  `IReceiptsApi`. No module calls another; no integration event is required. Forcing the
  outbox here would be over-engineering. The outbox drain remains motivated by Insights
  (V2), not this.
- **Not** live-camera. Live `getUserMedia` needs HTTPS, deferred in 0014. We use a
  file-upload input (`capture="environment"`) which opens the phone's native camera
  over plain HTTP. The deferred HTTPS work stays deferred.
- **Not** Wallet-aware. OCR reads whatever currency is printed; display conversion is
  the existing list/summary concern, unchanged.

## Decisions

- **OCR provider: a config-driven `IChatClient` over an OpenAI-compatible endpoint,
  defaulting to Google Gemini 2.0 Flash (free tier).** The port `IReceiptOcr` is a
  domain-shaped abstraction in Application returning `ParsedDraft`; no AI type leaks past
  Infrastructure. Infrastructure adapts an `IChatClient` built from
  **`Microsoft.Extensions.AI.OpenAI` 10.7.0** pointed at a configurable base URL, so the
  same adapter works against Gemini's OpenAI-compat endpoint (`…/v1beta/openai/`), a
  local Ollama (`localhost:11434/v1`), or real OpenAI — the provider is configuration,
  not code. Config keys: `Ocr:Endpoint`
  (default `https://generativelanguage.googleapis.com/v1beta/openai/`), `Ocr:Model`
  (default `gemini-2.5-flash`), `Ocr:ApiKey`. **Use `gemini-2.5-flash`, not
  `gemini-2.0-flash`** — the latter's free-tier quota is `0` (verified 429 `limit: 0`),
  while 2.5-flash is free and read a real Armenian receipt in ~7 s with exact numbers and
  ~85% product-name accuracy (the human-review step corrects the rest).
- **Why not Anthropic / why not local (recorded so it isn't re-tried):** the original
  plan (Claude vision) is blocked — Claude Pro grants no API access and no key was
  obtainable. A local VLM was then attempted to preserve $0 + full privacy, and
  **empirically rejected**: `qwen2.5vl:3b` (the largest vision model that fits the 8 GB
  RTX 5060) read the receipt's *numbers* perfectly but **could not read Armenian script
  at all** (merchant «ԱԼԵՔՍԱՐՍ» → garbage; every product name lost) and took ~170 s per
  receipt. Armenian is too low-resource for an 8 GB-class VLM. Gemini has strong Armenian
  and a genuinely free tier.
- **Cost & privacy:** $0 on Gemini's free tier (rate-limited, ample for one user). API
  key in **user-secrets** (`Ocr:ApiKey`), never committed. Tradeoff vs. the rejected
  local path: receipt images leave the machine to Google. If the key/endpoint is absent
  or unreachable, the module registers a **stub OCR** returning an empty draft, so the
  app still boots (e.g. the GPU-less Render deployment) and the upload→review→record flow
  still works via manual entry.
- **Synchronous OCR.** Upload blocks the Blazor circuit ~3–8s behind a spinner, then
  lands on the pre-filled review screen. No `BackgroundService`, no status polling, no
  `Parsing` race. Correct for a single user; revisit only if it annoys.
- **OCR produces a draft, never categories.** The model cannot know the user's category
  vocabulary. `ParsedDraft` carries description + amount (+ optional qty/unit); category
  assignment is the human review step, reusing the add-expense form's category pickers.
- **Image storage behind a port.** `IReceiptImageStore` writes bytes to a local,
  gitignored folder in dev (`receipts.imageRoot`, default under app data). Object
  storage is a prod concern, deferred. Images contain real financial data — privacy: the
  folder is gitignored; no image bytes ever touch the repo or seed data.
- **No cross-schema FK.** `Receipt.LinkedExpenseId` is an opaque `Guid?`. The link is
  set by the host after `RecordExpenseAsync` succeeds, via `IReceiptsApi.LinkAsync`.
- **Own DbContext + migrations.** `ReceiptsDbContext` with `HasDefaultSchema("receipts")`
  (also relocates its `__EFMigrationsHistory`, per the 0010 multi-module note). A
  `ReceiptsMigrationService` mirrors Wallet's: migrate-on-startup in Development only.

## Domain model

**Aggregate: `Receipt`** (`receipts.receipts`)

- Id: `ReceiptId` — `record struct` over `Guid`, `New(TimeProvider)` → v7.
- `ImageKey` (string) — storage key from `IReceiptImageStore`.
- `Status`: `Uploaded → Parsed → Linked`, plus `Failed`.
- `Draft` (`ParsedDraft?`, value object) — null until parsed; populated on `Parsed`.
- `LinkedExpenseId` (`Guid?`) — opaque, set on `Linked`.
- `UploadedOnUtc`, `ParsedOnUtc?` — timestamps via injected `TimeProvider`.

**Value object: `ParsedDraft`**

- `Merchant` (string?), `Date` (`DateOnly?`), `CurrencyCode` (string?).
- `Lines`: `IReadOnlyList<ParsedLine>` where `ParsedLine` = (`Description`, `Amount`,
  `QuantityValue?`, `QuantityUnit?`). No category — assigned at review.

**Invariants**

- `Parse(draft)` allowed only from `Uploaded`; sets `Parsed`.
- `MarkFailed(reason)` allowed only from `Uploaded`; sets `Failed`.
- `LinkTo(expenseId)` allowed only from `Parsed`; sets `Linked`. `Linked` is terminal.
- Empty `ImageKey` rejected at `Upload`.

**Domain events** (captured to the receipts outbox, write-only for now — symmetry with
the other modules, no drain): `ReceiptUploaded`, `ReceiptParsed`, `ReceiptParseFailed`,
`ReceiptLinked`.

## Application (vertical slices)

Three use cases → module stays **flat** (no per-aggregate subfolders yet).

- `UploadReceipt.cs` — command `(byte[] image, string contentType)`; stores image via
  `IReceiptImageStore`, creates `Receipt(Uploaded)`, **synchronously** calls
  `IReceiptOcr.ExtractAsync`, then `receipt.Parse(draft)` (or `MarkFailed`), saves,
  returns `ReceiptId` + the draft for the review screen.
- `LinkReceipt.cs` — command `(ReceiptId, Guid expenseId)`; `receipt.LinkTo(...)`, save.
- `GetReceipt.cs` — query for status + draft (review screen refresh / direct nav).

**Ports** (`Ports.cs`): `IReceiptRepository`, `IUnitOfWork`, `IReceiptOcr`,
`IReceiptImageStore`. **Exceptions** (`Exceptions.cs`): `ReceiptException` subtypes for
the invariant violations.

## Infrastructure

- `ReceiptsDbContext`, `ReceiptConfiguration` (map `ParsedDraft` + `ParsedLine`
  collection via `ValueConverter`/owned, per the EF 10 traps in CLAUDE.md — no complex
  ctor binding; single `private Receipt(ReceiptId)` ctor + backing fields).
- `ChatClientReceiptOcr : IReceiptOcr` — provider-agnostic. Builds the vision message
  (image `DataContent` + a structured-extraction prompt requesting JSON), calls the
  injected `IChatClient` (Gemini by default), parses JSON → `ParsedDraft`. A
  malformed/empty response degrades to an empty draft (user fills in manually), never
  throws into the UI.
- `StubReceiptOcr : IReceiptOcr` — registered when no API key; returns empty draft.
- `FileSystemReceiptImageStore : IReceiptImageStore`.
- `ReceiptsMigrationService`, `ReceiptsPersistence` (public anchor for the dispatcher
  Infrastructure scan, mirroring `ExpensesPersistence`).

## Public surface

`Roamint.Receipts.Contracts`:

- `IReceiptsApi`:
  - `Task<UploadReceiptResult> UploadAsync(UploadReceiptRequest, CancellationToken)`
  - `Task LinkAsync(Guid receiptId, Guid expenseId, CancellationToken)`
- `IReceiptsQueries`: `Task<ReceiptView?> GetAsync(Guid receiptId, CancellationToken)`.
- DTOs: `UploadReceiptRequest(byte[] Image, string ContentType)`,
  `UploadReceiptResult(Guid ReceiptId, ParsedDraftDto Draft, bool Parsed)`,
  `ParsedDraftDto`, `ParsedLineDto`, `ReceiptView`.
- `ReceiptsModule.AddReceiptsModule(IHostApplicationBuilder)` — wiring extension:
  persistence, dispatcher scan (Application + `ReceiptsPersistence` assembly), API +
  queries, `IReceiptImageStore`, and `IReceiptOcr` (Gemini / OpenAI-compatible when keyed, else stub).
  Registered from `Roamint.Web/Program.cs` alongside the other two modules.

## Web UI (`Roamint.Web`)

- New page `/receipts/new`: a file input
  `<input type="file" accept="image/*" capture="environment">`; on submit, POST bytes →
  `IReceiptsApi.UploadAsync` (spinner during the call).
- On result, render the **existing add-expense form** pre-filled from the draft (lines,
  date, currency). User assigns categories/tags, edits, confirms.
- Confirm handler: `IExpensesApi.RecordExpenseAsync(request)` → on success
  `IReceiptsApi.LinkAsync(receiptId, expenseId)`. Host orchestration; no module coupling.
- Entry point from the reports home / add flow (a "Scan receipt" affordance).

## Packages to pin (`Directory.Packages.props`)

- `Microsoft.Extensions.AI` `10.7.0` — Infrastructure (the `IChatClient` abstraction + DI).
- `Microsoft.Extensions.AI.OpenAI` `10.7.0` — Infrastructure (the OpenAI-compatible client
  used against Gemini's compat endpoint / Ollama / OpenAI).

All verified present on nuget.org via `dotnet package search`. (`Anthropic.SDK` is not
used — Claude API access was unavailable.)

## Open questions

- Gemini's Armenian accuracy on real thermal receipts — smoke-test on a sample before the
  adapter is finalized (the local VLM was rejected exactly this way).
- Fully-private fallback if cloud OCR becomes undesirable: Tesseract `hye` (dedicated
  Armenian OCR) → small local text LLM for structuring. More moving parts; revisit only
  if data-residency outweighs accuracy/effort.
- Image retention / thumbnails on the expense detail view — out of scope here; the link
  exists, surfacing the stored image is a follow-up.
- Re-scan / re-parse of a `Failed` receipt — currently terminal-ish; add a retry path if
  failures are common in practice.
- Prod image storage (object store) and prod migration strategy — deferred, as in 0005.

## Verification

- **Unit (Domain):** `Receipt` state-machine invariants — parse-from-non-Uploaded,
  link-from-non-Parsed, terminal `Linked`, empty-image rejection.
- **Application:** `UploadReceipt` with a **fake `IReceiptOcr`** (NSubstitute) returning
  a known draft → receipt ends `Parsed` with that draft; OCR throwing → `Failed`.
- **Infrastructure (Testcontainers):** round-trip a `Receipt` with a multi-line
  `ParsedDraft` through `ReceiptsDbContext`; outbox row written in the same transaction.
- **End-to-end (manual, phone over hotspot):** photograph a real receipt at
  `/receipts/new` → review screen pre-filled → assign categories → confirm → expense
  appears in the list and the receipt is `Linked`. Run with `Ocr:ApiKey` set (Gemini); the
  stub path verified by booting without the key (empty draft, manual fill still works).
