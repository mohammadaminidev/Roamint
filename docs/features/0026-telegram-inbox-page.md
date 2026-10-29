# 0026 — Telegram inbox page

Status: **BUILT**. Builds on [0024](0024-telegram-capture.md).

## Context

In 0024, a Telegram message that can't be recorded automatically becomes a row in the job bar (0019).
The first live run showed three problems with that:

- **The bar is in memory.** If you dismiss a row by accident or miss it, the message disappears from
  the UI until the app restarts and the startup sweep rebuilds the rows. The database still holds the
  item as `Parsed`, but nothing shows it.
- **There's no way to deal with a pile of messages.** A weekend of group chat can produce 20 or more
  reviews. The bar caps at 12 rows and only lets you review or dismiss one row at a time.
- **There's no "check now".** New messages arrive only on the one-minute pull timer, and a failed read
  is retried only on restart.

## Decisions

- **D1 — The page reads the database, not the job bar.** `/inbox` lists open inbox items straight from
  `receipts.inbox_items`. These are the `Received`, `Parsed` and `Failed` items, i.e. the same set the
  startup sweep uses. The page is the durable view: nothing that is still open can disappear from it.
  The bar stays an in-memory notification, never the record.
- **D2 — One summary row in the bar instead of one row per message.** The bar replaces its per-message
  Telegram rows with a single row: "N Telegram messages to review · Open". The row links to `/inbox`
  and is shown while N > 0. Receipt rows are unchanged. This fixes the 12-row cap for Telegram, and
  dismissing the row can no longer hide a message.
- **D3 — What each row offers depends on its status.**

  | Status | Row shows | Actions |
  |---|---|---|
  | `Parsed` | Sender, original text, sent time, draft amount/currency, matched item if any | **Review**, **Dismiss** |
  | `Failed` | Sender, text, "couldn't read" | **Retry**, **Dismiss** |
  | `Received` | Sender, text, "reading…" | none (it's in the backlog) |

  - **Review** builds the same capture seed as the processor and opens `/add?review=<id>`.
  - **Retry** puts the item back on the `InboxBacklog`.
  - **Dismiss** marks the item `Ignored` and clears its reaction on Telegram.
- **D4 — Dismiss is a real state change, so it's a domain change.** Today `Ignore()` is allowed only
  from `Received`/`Failed`. The case that matters here is a parsed message you've already entered by
  hand, or one that was never an expense, so `Parsed → Ignored` becomes legal. `Linked` stays final.
  - Dismiss reuses the existing `IgnoreInboxItem` slice. No new event is added: nothing downstream
    reacts to a dismissal.
- **D5 — "Check now" pulls from the relay immediately.** The page header has a refresh button. It wakes
  `InboxPuller`, using a signal on `InboxBacklog` that the timer loop also waits on, then reloads the
  list.
  - The relay is still drained only by the puller, never from a circuit, so the
    persist-then-ack ordering stays in one place.
- **D6 — Live updates are pushed, not polled.** `InboxProcessor` raises a `Changed` event on a
  singleton (`InboxBoard`) after it settles an item, and so does `Accept`. The page and the bar's
  summary row subscribe and re-query, the same pattern as `ReceiptJobs.Changed`.
- **D7 — Reviewing returns you to the inbox.** After saving an expense whose source is Telegram, `/add`
  navigates back to `/inbox`, so a pile can be worked through in sequence. Other capture sources keep
  today's behaviour of staying on the form.
- **D8 — The seed builder moves out of `InboxProcessor`.** The processor (needs-review path) and the
  page (Review) must build identical seeds. The shared "draft + learned match → `CaptureSeed`" logic
  becomes a small host helper, `InboxReviewSeed`, so the page never reaches into the processor.
- **D9 — Navigation.** The bottom nav has no free slot, so the page is reached three ways:
  - from the bar's summary row;
  - from a new "Telegram inbox" entry in the + capture sheet, which shows the open count;
  - by URL.

- **D10 — Components that sit beside a page get their own DI scope.** *(Found while building.)* The job bar
  and the + sheet render next to every page. They share the circuit's scoped `DbContext` with that page, and
  the first `/inbox` load crashed with EF's "a second operation was started" error. Both now read the open
  count through `IServiceScopeFactory`. The page itself serializes its database calls with a gate, because
  a `Changed` reload can arrive while a Dismiss is still in flight.
- **D11 — The list shows what was read, not the learned match.** A row shows the draft (amount, currency and
  the extracted description). Showing the learned match would cost one lookup per row. Review resolves the
  match when it builds the seed, so the form still opens with the learned name and category.

## Domain model

`InboxItem.Ignore()` now allows `Parsed` as well as `Received`/`Failed`. Nothing else changes: no new
aggregates, value objects or events.

## Public surface

`IInboxApi` (Receipts.Contracts):

- `ListOpenAsync` returns `IReadOnlyList<InboxItemDto>` instead of ids: one query instead of one
  `GetAsync` per row. The startup sweep selects `.ItemId`.
- The `IgnoreAsync` doc changes to "marks an item as not to be recorded (an unknown sender, or
  dismissed by the user)".

Host only (`Roamint.Web`):

- the `/inbox` page;
- the `InboxBoard` (changed event);
- the `InboxReviewSeed` helper;
- the pull-now signal on `InboxBacklog`.

## Open questions

- Should there be a "Dismiss all" bulk action? Left out for now: Dismiss is one tap per row, and a bulk
  action is easy to fire by mistake.
- Should the page also show recently settled items (Linked/Ignored, for example the last 7 days) as a
  log? Left out of this step.

## Verification

- **Domain test:** `Parsed → Ignored` is allowed; `Linked → Ignored` still throws.
- **Application test:** dismissing a parsed item via `IgnoreInboxItem`.
- **Persistence test:** `ListOpenAsync` returns DTOs, oldest first, excluding Linked/Ignored.
- **Host tests:** `InboxReviewSeed` produces the same seed the processor used to build (the existing
  `InboxProcessorTests` keep passing).
- **Done in the browser (2026-10-29).** I ran a second copy of the app with relay pulling and the bot token
  switched off, so it couldn't reach the relay or the Telegram group. It was pointed at the local database,
  where I added two throwaway items (deleted afterwards). I checked that:
  - the list rendered at phone width;
  - Review opened `/add` with the line, AMD and the payer filled in;
  - the bar's summary row counted both items and opened `/inbox`;
  - Dismiss removed the row;
  - Retry re-read the failed item, and the row disappeared live.

  Not checked: saving a review (it records a real expense) and Check now against the live relay.
- **Manual run:**
  1. Open `/inbox` and check that the dinner message shows, without a restart.
  2. Tap Review, save, and check that you land back on `/inbox` with the row gone and 👍 on Telegram.
  3. Send a new message, tap "Check now", and check that it appears within seconds.
  4. Dismiss a row and check that the reaction clears.
