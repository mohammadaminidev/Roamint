# 0024 — Outbox relay (drain + in-process integration-event publish)

## Context

Since 0005 every module's `DbContext` writes its aggregates' domain events into `<schema>.outbox_messages`
in the same transaction as the state change. Nothing reads those tables. The dispatcher (0003) can already
fan an `IIntegrationEvent` out to `IIntegrationEventHandler<T>`s, but nothing publishes one. This slice
closes the loop. A relay drains each module's outbox after commit, publishes through `IEventDispatcher`,
and stamps the row as processed.

**This reverses a recorded decision, so it needs explicit sign-off.** 0012 moved the drain to V3+
(2026-06-14). 0015 and 0019 both said they were "not the drain", and the README says the drain "lands with
its first real consumer (Insights or Wallet balances), not before". Today **zero** handlers exist. Shipping
the relay alone is infrastructure nobody exercises in production. See D1 for how this doc scopes that risk.

### What this is *not*

- **Not** a message broker. It is in-process, a single host, `IEventDispatcher`. No RabbitMQ or Wolverine.
- **Not** an event store. The outbox is a delivery queue. Consumers that need history **backfill their own
  state** (a projection rebuild from the publisher's public query API at deploy time). They never replay
  old outbox rows. This keeps the integration-event catalogue small and lets processed rows be pruned.
- **Not** the first consumer. The Insights expense projection (or Wallet balances) is its own slice and
  its own doc.
- **Not** multi-instance safe. There is one host (Oracle VM, 0023), and the relay assumes a single
  process. See D7.

## Decisions

### D1 — Scope: relay + one minimal event catalogue, consumer next

Two reasonable cuts:

| Option | Pros | Cons |
|---|---|---|
| **A. Relay alone (this doc)**, tested end-to-end by a test-only handler | Small, reviewable. The mechanics (ordering, retries, dead-letter) are reviewed without a domain feature on top | No production subscriber. The integration-event shapes are guessed before a consumer shapes them |
| **B. Relay + first consumer in one slice** | No dead code. Event payloads designed against a real need | Two concerns in one review. Insights is a new bounded context that needs its own design round first |

**Recommendation: A, conditional on the very next slice (0025) being the first consumer.** To limit
speculative contracts (0010 D6's concern), the catalogue in this slice is **only** `ExpenseRecorded` and
`ExpenseCorrected`, the two every candidate consumer (Insights, balances) needs, and is marked
*provisional* until 0025 confirms the payload. Wallet and Receipts get the relay wiring but **publish
nothing** yet (D3). If 0025 isn't next, don't merge this. Leave the README decision standing.

### D2 — Translate at capture time; the outbox stores the integration event, not the domain event

Today the payload is `JsonSerializer.Serialize(domainEvent)`. Draining that means deserializing domain
types, which **does not work**. `Money` is a `readonly struct` with a private ctor and get-only
properties, and `Currency` is a smart-enum class. `System.Text.Json` would silently materialize
`Money(0, null)`, not throw. Teaching STJ the kernel types (converters for `Money`, `Currency` and every
strongly-typed ID) would also freeze domain-event shapes as a persisted format.

So capture becomes **domain event → integration event → JSON**, in the same `SaveChanges`:

- Each module has an internal `IntegrationEventMap` in Infrastructure. It is a `switch` from domain event
  to `IIntegrationEvent?`, plus the stable discriminator (replacing today's `OutboxEventTypes`).
- `null` means *internal-only*: the event is **not written** to the outbox. The outbox now holds exactly
  what will be published.
- Payloads are primitive (`Guid`, `decimal`, ISO currency `string`, `DateOnly`), so they round-trip
  without kernel converters, and domain events stay freely refactorable.

**Trade-off:** domain events with no integration counterpart (category/tag lifecycle, receipt and wallet
events) stop being captured. That loses nothing anyone reads. The capture-can't-be-retrofitted argument
(0005) assumed consumers would replay history. Per *Not an event store*, they backfill instead.

### D3 — Integration events live in a per-module `IntegrationEvents` project

A consumer can't reference the publisher's `Contracts`, because `Roamint.Expenses.Contracts` references
Application + Infrastructure and would drag the whole module in transitively. New project
`src/Modules/Expenses/Roamint.Expenses.IntegrationEvents`. It references only
`Roamint.Application.Abstractions` and holds sealed records:

```csharp
public sealed record ExpenseRecordedIntegrationEvent(
    Guid EventId, Guid ExpenseId, decimal Total, string Currency, DateOnly OccurredOn) : IIntegrationEvent;

public sealed record ExpenseCorrectedIntegrationEvent(
    Guid EventId, Guid ExpenseId, decimal Total, string Currency, DateOnly OccurredOn) : IIntegrationEvent;
```

`EventId` is the outbox row id, and gives consumers the key to dedupe on (D5). The payload stays as thin as
today's domain event. Lines and categories are **not** included until 0025 proves it needs them.
Wallet/Receipts get no `IntegrationEvents` project until they publish something (no folders-of-one).

### D4 — One shared relay, per-module sources

The three `OutboxMessage` classes and configurations are byte-identical apart from a comment. New project
`src/Shared/Roamint.Outbox` (references EF Core Relational + Abstractions) owns:

- `OutboxMessage` + `OutboxMessageConfiguration` (`modelBuilder.ApplyOutbox()`). The table name is
  unchanged, and the schema comes from each context's `HasDefaultSchema`.
- `OutboxRelay<TDbContext>`: a `BackgroundService` registered per module by its persistence wiring
  (`builder.AddOutboxRelay<ExpensesDbContext>(map)`), so the host stays ignorant.
- The capture helper the three `SaveChanges` overrides currently duplicate.

The rejected alternative is a per-module relay in each Infrastructure (CLAUDE.md's layering note). That
would mean three copies of the retry/dead-letter logic, which is exactly the logic that must not drift.
The table is per module (no cross-schema FK, schema ownership intact). Only the code is shared.

**Migration trap:** moving the CLR type changes the entity's name in each model snapshot. The generated
migration must contain **only** the new columns (D6). If EF emits a drop/create of `outbox_messages`,
stop and fix the mapping. Don't hand-edit around it.

### D5 — Delivery: at-least-once, per-module FIFO, handlers idempotent

- **Loop:** poll every 2 s (`TimeProvider`-driven `PeriodicTimer`). Per tick, read up to 50 unprocessed
  rows `ORDER BY id`. UUIDv7 ids give occurrence order. Publish **one row at a time** in a fresh DI scope,
  and stamp `processed_on_utc` after `PublishAsync` returns.
- **Why not a post-commit nudge** (a channel the capture signals for near-zero latency)? No consumer is
  latency-sensitive, since projections and balances are fine at 2 s. Add one if a UI ever waits on a
  handler.
- **At-least-once:** a crash between publish and stamp re-delivers. Within one publish, if handler 2
  throws after handler 1 succeeded, the retry re-runs handler 1. **Handlers must be idempotent** (dedupe
  on `EventId`, or write via upsert). This is the trade for having no per-handler inbox table. One user
  and one process don't justify an inbox. Revisit when a non-idempotent side effect (email, external
  API) subscribes.
- **Order vs. poison:** on failure the row's `attempts` increments, `last_error` is set, and the tick
  **stops** for that module (preserving order: `Corrected` must not overtake `Recorded`). After
  **5 attempts** the row is dead-lettered (`dead_lettered_on_utc` stamped, logged as an error) and the
  relay moves past it. Backoff is the 2 s tick. It is not exponential, because five failures across ten
  seconds still identify a deterministic bug. A dead letter is fixed by hand (SQL reset of
  `dead_lettered_on_utc`/`attempts`) after the handler is fixed.

### D6 — Schema change (each module's migration)

`outbox_messages` gains `attempts int not null default 0`, `last_error text null`, and
`dead_lettered_on_utc timestamptz null`. The partial index becomes
`WHERE processed_on_utc IS NULL AND dead_lettered_on_utc IS NULL`.

The same migration also **marks every existing row processed** (`UPDATE … SET processed_on_utc = now()`).
They are domain-event payloads in the old format (D2), predate any consumer, and can't be deserialized.
Nobody missed anything.

### D7 — Single instance, no row locking

No `FOR UPDATE SKIP LOCKED`, because one host runs one relay per module. If a second instance is ever
deployed, both would publish the same rows, which is tolerable under D5's idempotency but wasteful. Add
`SKIP LOCKED` then. It is a one-line `FromSql` change, so this stays a documented assumption, not a
latent bug.

### D8 — Startup ordering and observability

- The relay is registered **after** the module's migration service, so in Development `StartAsync`
  migrates first (hosted services start in registration order). In Production the schema is migrated
  before the container starts (0023).
- Failures and dead letters log via `LoggerMessage` (CA1848). The relay opens one `Activity` per
  published message (`ActivitySource "Roamint.Outbox"`, registered in ServiceDefaults), so a handler's
  DB spans nest under it in the Aspire dashboard. No custom metrics: the dead-letter count is one SQL
  query.

### D9 — Retention

Processed rows are kept. At a few rows a day this is kilobytes a year. Pruning (delete processed rows
older than 30 days on the same timer) is a ten-line addition once it matters. It is listed as an open
question, not built.

## Domain model

No aggregate changes. Domain events are unchanged and stay internal. The new public types are the two
integration events in D3. The outbox row gains `Attempts`, `LastError`, `DeadLetteredOnUtc` and the
behaviours `MarkProcessed(now)` and `RecordFailure(error, now, maxAttempts)` (which dead-letters on the
cap). Both are guarded so a processed row can't fail and a dead-lettered row can't be processed.

## Public surface

- `Roamint.Expenses.IntegrationEvents`: `ExpenseRecordedIntegrationEvent` and
  `ExpenseCorrectedIntegrationEvent` (provisional, D1).
- `Roamint.Outbox` (shared, infrastructure-level): `ApplyOutbox(ModelBuilder)` and
  `AddOutboxRelay<TDbContext>(...)`. Consumed only by module Infrastructure projects, never by the host.
- No change to `IExpensesApi`, `IWalletApi`, `IReceiptsApi` or the host.

## Open questions

1. **Is 0025 the first consumer, and which one?** The Insights expense projection, the precursor to NL
   queries, is the obvious candidate. D1's recommendation stands or falls on this.
2. **Payload richness.** Will Insights need lines and categories in `ExpenseRecorded`, or a projection
   that backfills via `IExpensesQueries` and treats the event as a "go re-read expense X" signal? The
   second keeps events thin but needs a cross-module read API. Decide in 0025.
3. **Retention** (D9): prune now or when it matters.
4. **0023 doc missing from the repo.** `Dockerfile`, `.dockerignore` and `.gitignore` cite
   `docs/features/0023-oracle-deployment.md`, but it was never committed. D8 assumes Production migrates
   before the container starts. Confirm against 0023 when it lands.

## Verification

1. `dotnet build`: 0 warnings (warnings-as-errors).
2. Each module's new migration contains only `AddColumn` + the index swap + the `UPDATE` (D4 trap).
3. **Unit (`tests/Shared/Roamint.Outbox.Tests`):** `OutboxMessage` state transitions, covering
   processed, retry-then-dead-letter at the cap, and the guarded invalid transitions.
4. **Integration (Testcontainers, Expenses):** record an expense, and a test-only
   `IIntegrationEventHandler<ExpenseRecordedIntegrationEvent>` receives it with matching
   id/total/currency/date, and the row is stamped. A throwing handler leads to `attempts` incrementing,
   later rows staying unpublished (order), and a dead letter after 5 with later rows then flowing.
   Restarting the relay mid-batch re-delivers only unstamped rows.
5. **Capture:** creating a category writes **no** outbox row (internal-only, D2).
6. Manual: `dotnet run` AppHost. Record an expense on the phone, and `expenses.outbox_messages` shows
   `processed_on_utc` set within ~2 s. The Aspire trace shows the `Roamint.Outbox` publish span.
