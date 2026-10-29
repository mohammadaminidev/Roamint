# 0024 — Telegram capture: household group → relay inbox → expense

> **Hosting note.** Roamint stays local (0023 hosted deployment is deferred), so the
> Cloudflare relay below is the plan. If the app is ever hosted 24/7, the relay can be
> replaced by a direct webhook into the host (Tailscale Funnel, see 0023); everything
> downstream of "message stored" — parsing, matching, `PaidBy`, reactions — is unaffected.

## Context

A fourth capture channel, alongside manual entry, receipt OCR (0015) and clipboard order
import (0017). The user and their partner share a private Telegram group with a Roamint bot
and write expenses as free Persian text the moment they happen:

> من 15 هزار درام برای شام امشب دادم

Roamint turns each such message into an expense, records **who paid**, and the bot replies
in the thread so the chat itself shows what has been captured.

The constraint that shapes everything: **Roamint is locally hosted and is often off**
(weekends, trips). Telegram keeps undelivered bot updates for at most **24 hours**, and the
Bot API has **no history read** — a bot only ever sees messages pushed to it. So something
always-on must hold messages until Roamint comes back. That something is a deliberately
dumb **relay inbox** on Cloudflare (Worker + D1): Telegram pushes to it via webhook, it
stores the raw update, and Roamint drains it on startup and every minute while running.

### What this is *not*

- **Not** a separate app. All business logic — parsing, matching, recording, replies —
  lives in the monolith. The relay is storage with an HTTP face, ~100 lines, no domain.
- **Not** bill-splitting. Two people, one household budget, **attribution only** (who
  paid). No shares, no "who owes whom". If that is ever wanted it is its own module.
- **Not** a generic ingestion platform. The relay stores a *source-agnostic envelope* so a
  second source (e.g. bank SMS forwarded from the phone) is a new endpoint + a new parser,
  not a redesign — but only Telegram is built. Same registry-not-DSL stance as 0017.
- **Not** a userbot. Reading the group as the user's own account (MTProto) would avoid the
  relay but leaves a full-account session file on disk next to a public repo. Rejected.

## Decisions

### Relay (Cloudflare Worker + D1, `tools/inbox-relay/`)

- **Webhook in, pull out.** Telegram → `POST /telegram` (webhook). Roamint →
  `GET /inbox` + `POST /inbox/ack`. Once a webhook is set, `getUpdates` is unavailable;
  Roamint never talks to Telegram's update API, only to the relay.
- **Envelope table, not a Telegram table.**
  `inbox(id INTEGER PK, source TEXT, external_id TEXT, received_at TEXT, payload TEXT,
  UNIQUE(source, external_id))`. Telegram rows: `source = 'telegram'`,
  `external_id = '<chat_id>:<message_id>'`, `payload` = the raw update JSON. Telegram
  retries a webhook on non-2xx, so the unique key makes re-delivery a no-op
  (`INSERT … ON CONFLICT DO NOTHING`), and the Worker returns 200 right after the insert.
- **Ack = delete.** Roamint persists items in its own schema *before* acking, so the relay
  holds only undrained messages. Nothing accumulates on Cloudflare.
- **Three locks.** (1) `setWebhook` with `secret_token`; the Worker rejects requests
  without a matching `X-Telegram-Bot-Api-Secret-Token`. (2) A chat-id allowlist — updates
  from any other chat are dropped (200, not stored). (3) The pull/ack endpoints require a
  bearer API key. All three are Worker secrets (`wrangler secret put`), never in the repo.
- **Instant "seen" signal.** On store, the Worker sets a 👀 reaction on the message
  (`setMessageReaction`). This is the one thing it does beyond storing: over a weekend it
  tells the partner "received, will be recorded when Roamint runs". It needs the bot token
  as a Worker secret — acceptable, the token is already needed once for `setWebhook`.
- **Stored only: `message` updates with text.** Edits, photos, joins, etc. are ignored at
  the relay (see Open questions for edits).
- **TypeScript, not .NET.** Workers are JS-native; a .NET Azure Function + Table Storage
  would be the same shape with more setup. The relay is small enough that the language
  boundary costs nothing.

### Roamint side

- **Lives in the Receipts module** — already the "external sources → drafts" module
  (0017). New sibling aggregate `InboxItem` next to `Receipt` and `ImportedOrder`, sharing
  `ParsedDraft`. A new module would duplicate DbContext/migrations/outbox for one
  aggregate. The module-name stretch noted in 0017 grows; still not worth a rename.
- **Drain = host background service**, mirroring 0019. `InboxPuller : BackgroundService`
  runs at startup and every 60 s: `GET /inbox` → `IInboxApi.AcceptAsync(envelopes)`
  (idempotent on `(Source, ExternalId)`) → `POST /inbox/ack` → enqueue the new item ids.
  The relay being unreachable is logged and retried next tick; never fatal.
- **Processing = host worker**, serial like `ReceiptProcessingWorker` (it crosses
  Receipts → Expenses, which is host orchestration per 0019):
  1. `IInboxApi.ParseAsync(id)` — Receipts asks Gemini (existing `IChatClient`) to extract
     `{ isExpense, amount, currency, description, date }` and maps it to a one-line
     `ParsedDraft`. `isExpense = false` → item `Ignored`, no reply (normal chat is noise).
  2. Host resolves the line via `IExpensesQueries.MatchItemAsync` (exact, 0021).
  3. **Auto-record only when deterministic**: currency is explicit, the line exact-matches
     the learned item vocabulary (so it carries a category), and the 0022 duplicate guard
     finds no twin. Then `IExpensesApi.RecordAsync` → `IInboxApi.LinkAsync` → react 👍 +
     detail reply.
  4. Otherwise → the item becomes a **reviewable job in the existing `ReceiptJobBar`**
     (0019); react ✍. Confirming it in the app records + links and the reaction flips
     to 👍.
  The vocabulary learns on confirm (0016/0021), so "شام" needs review once and is
  automatic from then on. This keeps 0021's lesson — no LLM-guessed categories — while
  making the common case zero-touch.
- **The bot's reaction is the message's status light.** A bot can hold one reaction per
  message, and `setMessageReaction` *replaces* it, so the reaction tracks the item's
  lifecycle: 👀 stored by the relay → 👍 recorded / ✍ needs review / 🤷 failed to parse.
  Ignored chatter gets its 👀 cleared (empty reaction list) — the relay can't tell
  chatter from expenses, Roamint can. A glance at the chat shows what is pending.
  Reactions are limited to Telegram's fixed emoji set (✅/📝 are not in it, hence 👍/✍),
  and the group must allow those reactions.
- **A reply only where it carries information.** On auto-record the bot replies once with
  what it understood — `15,000 AMD · Dining · paid by <member>` — because the amount was
  extracted by an LLM and the reply is the human check. Review and failure get the
  reaction only; the detail is in the app.
- **The message date is the anchor, not the processing time.** "امشب" in a message sent
  Saturday and drained Monday means Saturday. The prompt gets the message's own
  timestamp (converted to Asia/Yerevan) as "now"; a missing date in the extraction
  defaults to that day, never to `TimeProvider` today.
- **LLM output is validated, not trusted.** Amount > 0; currency ∈ the supported set
  (AMD/USD/USDT — `درام`, `دلار`, `تتر` are aliases the prompt lists); date not in the
  future relative to the message. A failed validation → review, not rejection.
- **Who paid = sender → household member.** Members are configuration, not data:
  `Household:Members = [{ Key, DisplayName, TelegramUserId }]` (values in user-secrets;
  anonymized in examples). A sender not in the list → `Ignored` (defence in depth behind
  the relay's chat allowlist), checked *before* the extractor call. **No members configured
  at all → items are left unsettled, not ignored**: otherwise starting the app with the
  relay configured but the household not yet set would ignore every pending message for
  good. The startup sweep picks them up once members exist.
- **`PaidBy` lands on `Expense`.** Attribution is a fact about the payment, so it belongs
  to the Expenses aggregate: an optional `PaidBy` (the member `Key`, a short validated
  string — not a `MemberId` aggregate; two configured people do not warrant one). `null`
  = unattributed, which is every historical row and any entry made without picking a payer.
  The add form has a payer picker (one segment per member; tap again to clear). The
  monthly summary gains a "Paid by" breakdown — an EF `GROUP BY` like the category summary,
  shown only once at least one expense in the month is attributed.
- **Feedback goes through Receipts.** Only Receipts knows an item's source and reply
  address, so it exposes `IInboxApi.ReactAsync(id, InboxReaction)` and
  `IInboxApi.ReplyAsync(id, text)`; Infrastructure implements them for Telegram with a
  typed `HttpClient` calling `setMessageReaction` and `sendMessage` (with
  `reply_parameters.message_id`). The emoji mapping lives in that adapter, so the
  contract speaks in states (`Recorded`, `NeedsReview`, `Failed`, `None`), not emoji.
  **No Telegram SDK package** — two endpoints don't earn a dependency. Bot token from
  `Telegram:BotToken` in user-secrets; unset → a no-op notifier (everything else works).
- **The extractor reuses the OCR `IChatClient`** (`Ocr:ApiKey`). Unset → an extractor that
  throws, so items become `Failed` (🤷) and are retried by the startup sweep once a key is
  set, instead of being guessed at.
- **Restart safety.** Unsettled items (`Received`, `Parsed`-awaiting-review, `Failed`) are
  re-enqueued on every start. A parsed item is *re-evaluated*, not re-read: if its
  description was learned since, it auto-records then. A review row dismissed from the bar
  without confirming comes back on the next start — the persisted review queue.

## Domain model

### Receipts — `InboxItem` (aggregate, new)

- `InboxItemId` (strongly-typed, v7).
- `Source` (`"telegram"`), `ExternalId` (`"<chat>:<message>"`) — dedup key, unique index.
- `SenderExternalId` (Telegram user id as string), `Text`, `SentOnUtc` (message time),
  `ReceivedOnUtc` (drain time), `Payload` (raw JSON, `jsonb` — kept for re-parse).
- `Draft : ParsedDraft?`, `LinkedExpenseId : Guid?`, `Status`.

State machine:

```
Received ──Parse──▶ Parsed ──LinkTo──▶ Linked
    │                 
    ├──Ignore──▶ Ignored      (not an expense / unknown sender)
    └──MarkFailed──▶ Failed   (LLM call failed — re-parseable, like Receipt)
```

Invariants: `Parse`/`Ignore`/`MarkFailed` only from `Received` or `Failed` (a failed
read is retried); `LinkTo` only from
`Parsed`, non-empty expense id; a parsed draft has exactly one line (one message = one
payment; multi-item messages are an open question). Events: `InboxItemReceived`,
`InboxItemLinked`.

### Expenses — `Expense` (modified)

- `PaidBy : string?` — trimmed, blank → null, max 32 chars, set in `Record`/`Correct`.
  Not validated against the member list in Expenses at all: membership is host
  configuration, and the host is the only writer that knows it (the picker offers only
  members; Telegram maps only known senders). A port into Expenses just to re-check it
  would be a premature interface.

## Public surface

- `Roamint.Receipts.Contracts` — new `IInboxApi`:
  - `Task<IReadOnlyList<Guid>> AcceptAsync(IReadOnlyList<InboxEnvelopeDto>, ct)` — returns
    ids of *newly* stored items (duplicates skipped).
  - `Task<ParseInboxItemResult> ParseAsync(Guid id, ct)` → `(string Status, ParsedDraftDto? Draft)`;
    the payer is mapped by the host from the item's sender, not by Receipts.
  - `Task IgnoreAsync(Guid id, ct)` (unknown sender), `Task LinkAsync(Guid id, Guid expenseId, ct)`,
    `Task ReactAsync(Guid id, InboxReactionKind reaction, ct)`, `Task ReplyAsync(Guid id, string text, ct)`.
  - `Task<InboxItemDto?> GetAsync(Guid id, ct)`, `Task<IReadOnlyList<Guid>> ListOpenAsync(ct)`.
  - `InboxEnvelopeDto(string Source, string ExternalId, string Payload)`.
- `Roamint.Expenses.Contracts` — `RecordExpenseRequest` / `CorrectExpenseRequest` gain
  `string? PaidBy = null`; new `IExpensesQueries.GetPayerSummaryAsync` → `PayerSpendItem`.
- Host-only: `InboxPuller` → `InboxBacklog` → `InboxProcessingWorker` running
  `InboxProcessor` per item; `InboxRelayClient` (typed `HttpClient`); `Household` over
  `Household:Members` options. `ReceiptJobs.AddReady` adds an inbox review row;
  `CaptureSource.Inbox` + `CaptureSeed.PaidBy` carry it to the add form.
- Host config (Roamint.Web user-secrets, never committed): `InboxRelay:BaseUrl` (the
  `workers.dev` URL, no trailing path) and `InboxRelay:ApiKey` (the relay's `INBOX_API_KEY`).
  Either missing → the puller logs once and stays idle; the app runs normally.
- Relay HTTP: `POST /telegram`, `GET /inbox?limit=N` (oldest first, max 100), `POST /inbox/ack { ids: [...] }`. Setup runbook: `tools/inbox-relay/README.md`.

## Delivery steps (one reviewable change each)

1. ✅ Relay: Worker + D1 schema + `wrangler.jsonc` + README for `setWebhook`/secrets (`tools/inbox-relay`, 17 tests in the Workers runtime).
2. ✅ Expenses: `PaidBy` on `Expense` + `AddPaidBy` migration + request DTOs + payer picker on the form.
3. ✅ Receipts: `InboxItem` aggregate + `Inbox/` slices (`AcceptInboxEnvelopes`, `ParseInboxItem`,
   `IgnoreInboxItem`, `LinkInboxItem`, `ReactToInboxItem`, `ReplyToInboxItem`, two queries) +
   `AddInboxItems` migration.
4. ✅ Receipts Infrastructure: LLM extractor prompt + Telegram notifier (reaction + reply).
5. ✅ Host: `InboxPuller` + `InboxProcessingWorker` + `InboxProcessor` + `ReceiptJobBar` inbox rows.
6. ✅ Summary: per-payer breakdown.

Steps 2–6 landed together; verified by unit/integration tests (domain, application,
Postgres persistence, extractor parsing, host processor). The live end-to-end run with the
real bot is the manual check under Verification.

## Open questions

- **Household vs. personal budget.** Assumed: both people's spending is one household
  budget with a per-payer breakdown. If the partner's spending should be *excluded* from
  "my" totals by default, the summary needs a payer filter instead of a breakdown.
- **Edited messages.** A typo fixed in Telegram after capture: ignore (fix in the app) or
  re-parse and `Correct` the linked expense? Ignored in V1.
- **Multi-item messages** ("۵ هزار نون و ۲۰ هزار گوشت"): one expense with two lines is a
  natural extension of the one-line rule; deferred until it actually happens.
- **Undo from chat.** Replying "❌" to a ✅ to void the expense — nice, deferred.
- **Relay retention cap.** If Roamint is off for months the inbox grows unbounded; a
  30-day prune is trivial but unnecessary at two people's volume.

## Verification

- **Relay:** `curl` with wrong/missing secret header → 401, nothing stored; same update
  posted twice → one row; other chat id → 200, nothing stored; pull returns rows; ack
  deletes them.
- **Domain:** `InboxItem` transitions and rejections; `Expense.PaidBy` trimming/length.
- **Application:** `AcceptInboxItems` skips duplicates; `ParseInboxItem` with a fake
  extractor → `Parsed` / `Ignored` / `Failed`; message date (not clock) drives the draft
  date.
- **Host:** fake relay + fake extractor → matched line auto-records with `PaidBy`, reacts
  👍 and replies with the detail line; unmatched line becomes a bar job and reacts ✍
  (flipping to 👍 on confirm); a 0022 twin goes to review; chatter clears 👀.
- **End-to-end (manual):** stop Roamint → both people post in the group (expenses + one
  chat message) → 👀 appears on all → start Roamint next day → expenses dated to the
  *message* day, correct payer, 👍 + detail reply on expenses, 👀 gone from the chat
  message; monthly summary shows per-payer totals.
