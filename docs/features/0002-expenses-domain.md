# 0002 — Expenses module: domain layer

**Status:** In progress (domain layer)
**Lands in:** `src/Shared/Roamint.SharedKernel/`, `src/Modules/Expenses/Roamint.Expenses.Domain/`, `tests/Modules/Expenses/Roamint.Expenses.Domain.Tests/`

## Context

The Expenses module is the headline bounded context — it replaces the user's Excel workflow for
tracking daily multi-currency spending (cash USD, cash AMD, USDT). This feature delivers the **pure
domain layer only**: aggregates, value objects, invariants, and domain events, plus the shared-kernel
primitives they stand on and the first unit-test project.

Persistence (EF Core), the in-process dispatcher, and the transactional outbox are intentionally **out
of scope** here. They form the next slice, which also enables the add-expense form (V1 roadmap item 2).
Splitting this way keeps the step small and reviewable and lets the domain be exercised in isolation
with fast, infrastructure-free unit tests — the discipline this repo is meant to demonstrate.

## Decisions

- **Domain-only slice.** No EF, no outbox, no dispatcher yet. Domain events are raised and buffered on
  the aggregate; translating them to integration events and persisting them via the outbox is the next
  slice's job.
- **`Budget` deferred.** CLAUDE.md lists Budget among the module's aggregates, but it pairs naturally
  with the monthly-summary / budget-alert features and is not needed to record spending. Deferred to a
  later slice.
- **No `Item` catalog aggregate in V1.** CLAUDE.md listed `Item` as an aggregate, but for a one-handed
  mobile tracker the simplest faithful model is: an `Expense` optionally carries **line items as owned
  entities** (`ExpenseLine`), not references into a separate catalog. Item autocomplete becomes a
  **read-model concern** over past lines in a later feature rather than a write-side aggregate. This
  updates the CLAUDE.md aggregate list; revisit if a first-class item catalog earns its keep.
- **Expense records currency only, never a wallet.** `Expense.Amount` is `Money` carrying the paid
  currency. The Wallet module derives balances later by subscribing to `ExpenseRecorded` integration
  events. No cross-module coupling and no cross-schema reference now — honors "Wallet de-emphasized".
- **No FX in the domain.** An expense stores exactly one `Money` in its original currency. Conversion to
  display currencies (AMD / USD / IRR-toman) is driven by the Wallet rate timeline and is a **read**
  concern; it never mutates or is stored on the Expense aggregate.
- **`Money`/`Currency` live in the shared kernel**, since Wallet and Insights will need them too.
  Strongly-typed IDs stay **per module** (trivial `record struct` wrappers) to avoid a shared dumping
  ground and keep each context's identity types in its own namespace.
- **`Currency` is a smart-enum value object** (`Code` + `MinorUnits`) rather than a `System.Globalization`
  type: the supported set is small and fixed (USD, AMD, USDT, IRR), and USDT is not an ISO currency.
- **`TimeProvider` injected into ID and aggregate factories** rather than `DateTime.UtcNow`, both for the
  Guid v7 timestamp and for the "no future expense" guard — keeps the domain deterministic and testable.
- **Test framework: xUnit v3** + Shouldly, with `FakeTimeProvider` for the clock. Resolves the xUnit
  v2-vs-v3 open question carried from 0001 in favor of the current generation for a greenfield project.

## Domain model

### Shared kernel (`Roamint.SharedKernel`)

- `IDomainEvent` — marker interface for domain events.
- `Entity<TId>` — identity equality on `Id`.
- `AggregateRoot<TId> : Entity<TId>` — buffers domain events; `Raise`, `DomainEvents`, `ClearDomainEvents`.
- `Currency` — value object: `Code` (e.g. `"USD"`), `MinorUnits` (decimal places). Predefined statics
  `USD(2)`, `AMD(0)`, `USDT(2)`, `IRR(0)`. `Currency.FromCode(code)` throws `DomainException` on unknown.
- `Money` — `readonly record struct (decimal Amount, Currency Currency)`.
  - `Money.Of(amount, currency)` rounds to the currency's minor units (`MidpointRounding.AwayFromZero`),
    rejects `NaN`/`Infinity`.
  - `+` / `-` require matching currency, else `CurrencyMismatchException`; `*` by `decimal`;
    `Zero(currency)`; `IsPositive`.
- `DomainException` (base) and `CurrencyMismatchException : DomainException`.

### Expenses.Domain

**Strongly-typed IDs** — `readonly record struct` over `Guid`, `static New(TimeProvider) =>
new(Guid.CreateVersion7(now))`: `ExpenseId`, `CategoryId`, `ExpenseLineId`.

**`Category : AggregateRoot<CategoryId>`**
- State: `Name`, `Status` (`Active` | `Archived`).
- `Create(id, name)` — trims, guards non-empty and max length; raises `CategoryCreated`.
- `Rename(name)` — guarded; no-op if unchanged; raises `CategoryRenamed`.
- `Archive()` — idempotent; raises `CategoryArchived`.
- *Merge* is deferred to the category-management feature (it re-points existing expenses, a read/process
  concern that crosses aggregate boundaries).

**`Expense : AggregateRoot<ExpenseId>`**
- State: `CategoryId`, `Money Amount`, `DateOnly OccurredOn`, `string? Note`,
  `IReadOnlyList<ExpenseLine> Lines`.
- Invariants: amount strictly positive; `OccurredOn` not in the future (vs. injected clock); when lines
  are present, every line's currency matches `Amount.Currency` and the line totals sum exactly to
  `Amount`.
- `Record(id, categoryId, amount, occurredOn, clock, note?, lines?)` — enforces invariants; raises
  `ExpenseRecorded`.
- `Recategorize(categoryId)` — no-op if unchanged; raises `ExpenseRecategorized`.
- `Correct(newAmount, occurredOn, note?, lines?)` — re-runs the same invariants for an edit; raises
  `ExpenseCorrected`.
- `ExpenseLine` (owned entity): `Id`, `Description`, `Quantity` (decimal), `UnitPrice` (`Money`),
  `LineTotal => UnitPrice * Quantity`. Guards: description non-empty, quantity > 0, unit price positive.
- `ExpenseLineDraft(Description, Quantity, UnitPrice)` — input record the aggregate turns into entities.

### Domain events

`CategoryCreated`, `CategoryRenamed`, `CategoryArchived`, `ExpenseRecorded`, `ExpenseRecategorized`,
`ExpenseCorrected` — records implementing `IDomainEvent`, carrying ids and `Money` where relevant.

## Public surface

Nothing is exposed to the web host or other modules yet — there is no `Contracts` project in this slice.
The domain assembly is consumed only by its own Application/Infrastructure layers in later slices. The
shared kernel exposes `Money`, `Currency`, the aggregate/entity bases, `IDomainEvent`, and the domain
exceptions for reuse by every module.

## Open questions

- **Outbox event shape.** Which domain events become *integration* events, and their flattened payloads,
  is decided in the persistence slice (e.g. `ExpenseRecorded` for Wallet/Insights).
- **Will an Item catalog ever earn its place?** Re-evaluate once the add-expense form and autocomplete
  read model exist; promote to an aggregate only if write-side item identity is actually needed.
- **Amount-correction granularity.** `Correct` currently revises the whole expense; if the UI needs
  finer-grained line edits, split into targeted operations later.

## Verification

1. `dotnet restore` — clean, no NU1507.
2. `dotnet build` — 0 warnings, 0 errors (warnings-as-errors is on).
3. `dotnet test tests/Modules/Expenses/Roamint.Expenses.Domain.Tests` — all green.
4. `Roamint.Expenses.Domain` references only `Roamint.SharedKernel`; the web host builds untouched.

## What's next

The Expenses **persistence + dispatch** slice: EF Core schema (`expenses.`), the hand-rolled
command/query/event dispatcher, and the transactional outbox that turns the domain events above into
integration events. That slice closes V1 roadmap item 1 and unblocks the add-expense form (item 2).
