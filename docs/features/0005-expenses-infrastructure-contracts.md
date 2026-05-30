# 0005 — Expenses module: Infrastructure + Contracts

**Status:** Done
**Lands in:** `src/Modules/Expenses/Roamint.Expenses.Infrastructure/`,
`src/Modules/Expenses/Roamint.Expenses.Contracts/`,
`tests/Modules/Expenses/Roamint.Expenses.Infrastructure.Tests/`,
`src/Bootstrap/Roamint.Web/`, `src/Shared/Roamint.Application.Abstractions/` (one idempotency tweak),
`Directory.Packages.props`, `Roamint.slnx`

## Context

The domain (0002) guards expenses and categories; the dispatcher (0003) is the command/query/event seam;
the application (0004) wired the write-side use cases against repository **ports** (`ICategoryRepository`,
`IExpenseRepository`, `IUnitOfWork`) with in-memory fakes. This slice supplies the **real persistence**
behind those ports and the **public front door** the Blazor host calls — closing V1 roadmap item 1 and
unblocking the add-expense form.

Two projects appear: **Infrastructure** (EF Core mappings for the `expenses.` schema, concrete
repositories/unit-of-work, the outbox table + capture) and **Contracts** (the primitive-DTO facade plus
`AddExpensesModule`, the only Expenses project the web host references).

## Decisions

### Project graph

```
Roamint.Web ──► Expenses.Contracts            (host sees Contracts ONLY)
Expenses.Contracts ──► Expenses.Application, Expenses.Infrastructure,
                       Expenses.Domain, Application.Abstractions, SharedKernel
Expenses.Infrastructure ──► Expenses.Application, Expenses.Domain,
                            Application.Abstractions, SharedKernel, EF Core, Npgsql
Expenses.Application ──► Expenses.Domain, Application.Abstractions   (existing)
Expenses.Domain ──► SharedKernel                                    (existing)
```

- **Contracts is the module's composition surface** and may legitimately reference every internal layer —
  that is what `AddXxxModule` is *for*. The architecture rule ("host references only Contracts") constrains
  the **host**, not Contracts. The host gains no *reason* to touch internal types because the facade
  exposes primitives only.
- **Infrastructure stays persistence-only.** It exposes a `public AddExpensesPersistence(IHostApplicationBuilder)`
  so EF Core / Npgsql / Aspire references never leak into Contracts; `AddExpensesModule` calls it.

### Q1 — Outbox: capture now, drain when there's a consumer  *(decided)*

- **Capture is built now; the drain is deferred.** Inside the module's single `SaveChanges`, buffered
  domain events on tracked aggregates are serialized into `expenses.outbox_messages` **in the same
  transaction** as the aggregate, then `ClearDomainEvents()`. One `SaveChanges` is one transaction, so
  atomicity is free. **Why now:** atomic capture is the part that cannot be retrofitted — a write that
  commits state without its events is a permanent gap. **Why defer the drain:** Wallet and Insights are
  *Not started*, so a background dispatcher would deliver events to zero subscribers. Building it now would
  be speculative plumbing.
- **In 0005 the outbox is a write-only, append-only event log.** Nothing reads it yet. The drain slice
  (lands with the first consumer) adds: a `BackgroundService` poll + startup sweep, the domain→integration
  **translation**, dispatch via `IEventDispatcher`, `processed_on_utc` marking, idempotency, and
  retry/error columns. That keeps the integration-event *contracts* unwritten until a consumer defines
  their shape — no speculative public events.
- **Capture mechanism:** override `SaveChangesAsync` in `ExpensesDbContext` (single context, explicit,
  no shared abstraction yet). If a second module needs the same, extract a `SaveChangesInterceptor` then —
  not before.
- **Payload:** `System.Text.Json` to a `jsonb` column, tagged with a **stable discriminator string**
  (e.g. `expenses.expense-recorded`) via a small CLR-type↔string map in Infrastructure — *not* the
  assembly-qualified name (brittle across refactors). Only the write half of the map runs in 0005;
  defining stable discriminators now prevents churn when the drain reads them.

`expenses.outbox_messages` (minimal, forward-compatible):

| column            | type          | note                                   |
|-------------------|---------------|----------------------------------------|
| `id`              | `uuid` PK     | `Guid.CreateVersion7` — time-ordered   |
| `type`            | `text`        | stable discriminator                   |
| `payload`         | `jsonb`       | serialized domain event                |
| `occurred_on_utc` | `timestamptz` | `TimeProvider.GetUtcNow`               |
| `processed_on_utc`| `timestamptz` null | reserved; the drain sets it       |

Retry/attempt/error columns are added *with* the drain, by migration.

### Q2 — Contracts boundary: primitive-DTO facade  *(decided)*

- **`IExpensesApi` + primitive request DTOs are the public surface.** Strongly-typed IDs (`CategoryId`,
  `ExpenseId`) and the Application command records stay **module-internal**. The host speaks
  `Guid`/`string`/`decimal`/`DateOnly`. Rationale: typed IDs exist for *intra-module* invariant safety;
  at the boundary a `Guid` is the contract. Re-exporting commands would drag Domain types to the host and
  make every internal refactor a boundary change.
- **The facade implementation lives in Contracts** and maps DTO → Application command → `ICommandDispatcher`.
  It is not a redundant layer over the dispatcher: the dispatcher routes a command to its handler *inside*
  Application; the facade is the module's *external* API and hides that a dispatcher even exists. It earns
  its place via real mapping (primitive → typed, and the spot where `Currency.FromCode` surfaces a bad code
  as a boundary error).
- **One interface, justified by a real test seam** — Blazor components fake `IExpensesApi`. Consistent with
  CLAUDE.md "interface only with a real second implementation or test seam." Write-side only for now; when
  read models arrive, decide whether they join `IExpensesApi` or a separate read API.
- **Errors propagate as `DomainException`/`*NotFoundException` for now.** Whether the boundary should return
  a `Result<T>` instead of throwing is a UI concern — deferred to the add-expense-form slice (Open
  questions). The host transitively references `SharedKernel` to catch `DomainException`; acceptable (it is
  the shared kernel, not a module internal).

### EF Core mappings

- **`ExpensesDbContext`, default schema `expenses`.** EF Core 10 + Npgsql; **writes only** — no Dapper in
  this slice (no query handlers exist yet; Dapper arrives with the read models).
- **Strongly-typed IDs** (`record struct` over `Guid`) mapped via `ValueConverter`s registered in
  `ConfigureConventions` so every `CategoryId`/`ExpenseId`/`ExpenseLineId` round-trips to `uuid` without
  per-property config.
- **`Money` → `ComplexProperty`** (no identity): `amount numeric(19,4)` + `currency text` (converted via
  `Currency.Code` ↔ `Currency.FromCode`). Single precision covers all currencies; the domain already
  rounds to minor units. Fallback to `OwnsOne` only if a complex-type + nested-converter limitation bites.
- **`Expense.Lines` → `OwnsMany`** into `expenses.expense_lines` (own PK `ExpenseLineId`, FK to expense,
  `UnitPrice` mapped as the same `Money` complex type). EF uses the private `_lines` backing field
  (`PropertyAccessMode.Field`) since `Lines` is read-only. `LineTotal`/`IsPositive` are ignored (computed).
- **`Category.Status` → `text`** (`"Active"`/`"Archived"`) via `HasConversion<string>()` — readable in DB
  diagnostics over an opaque int.
- EF binds through the existing **private EF constructors** (`Category(id)`, `Expense(id)`). `DomainEvents`
  is ignored in mapping; the `SaveChanges` override drains it.
- **Optimistic concurrency via Npgsql `xmin`** (`UseXminAsConcurrencyToken()`) on both aggregates — one
  line, no extra column, and it guards the real phone-+-laptop concurrent-edit path
  ([[phone-input-deployment]]). Recommended; flag in Open questions if you'd rather skip for V1.

### Migrations

- EF Core migrations live in **Infrastructure** (migrations assembly). Initial migration creates the
  `expenses` schema, both aggregate tables, the owned line table, and `outbox_messages`.
- **Dev:** applied at startup via a hosted migration step (`MigrateAsync`) behind the Aspire AppHost.
  Production strategy (startup-migrate vs. migration bundle) is deferred — single-user, not shipping yet.
- One Postgres instance, **schema-per-module** (not database-per-module): the Aspire-provided connection is
  shared; Expenses owns only the `expenses` schema. Connection resolved by the AppHost resource name
  (confirmed against `AppHost.cs` at implementation).

### Module wiring + one Shared tweak

- **`AddExpensesModule(this IHostApplicationBuilder builder)`** registers: the Aspire Npgsql
  `ExpensesDbContext`; EF-backed `ICategoryRepository`/`IExpenseRepository`/`IUnitOfWork`; the
  `IExpensesApi` facade; and the module's handlers via `AddApplicationDispatcher(<Application assembly>)`.
  The host's `Program.cs` calls just `builder.AddExpensesModule()` and knows nothing else.
- **It hangs off `IHostApplicationBuilder`, not `IServiceCollection`** as CLAUDE.md's prose says — needed to
  use Aspire's `AddNpgsqlDbContext` (config + health + telemetry). Deliberate deviation; I'll update
  CLAUDE.md's wording on approval.
- **Shared tweak (needs this doc's approval — touches `src/Shared`):** `AddApplicationDispatcher` registers
  the three dispatchers with `AddScoped`, so each module calling it would **duplicate** them. Change those
  three to `TryAddScoped` (handler scans stay additive). This makes per-module registration safe and keeps
  modules self-contained — the cleaner alternative to making the host enumerate every module's Application
  assembly (which would re-couple the host to Application). Covered by 0003's test suite plus a new
  "called twice → single dispatcher registration" case.

### Tests

- New **`Roamint.Expenses.Infrastructure.Tests`** using **Testcontainers.PostgreSql** (first project that
  needs a real Postgres — introduces the pin per CLAUDE.md). Covers the mappings and outbox capture against
  a real database; in-memory provider would not exercise `jsonb`, `xmin`, schema, or the owned-collection
  SQL that is the whole point.

## Domain model

No new domain types. This slice persists the 0002 aggregates (`Category`, `Expense`, owned `ExpenseLine`)
and the kernel's `Money`/`Currency`. The only new *stored* type is the infrastructure `OutboxMessage`
row — not a domain concept.

## Public surface

`Roamint.Expenses.Contracts` (the host's entire view of the module):

```csharp
public interface IExpensesApi
{
    Task<Guid> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct);
    Task RenameCategoryAsync(Guid categoryId, string newName, CancellationToken ct);
    Task ArchiveCategoryAsync(Guid categoryId, CancellationToken ct);

    Task<Guid> RecordExpenseAsync(RecordExpenseRequest request, CancellationToken ct);
    Task RecategorizeExpenseAsync(Guid expenseId, Guid categoryId, CancellationToken ct);
    Task CorrectExpenseAsync(Guid expenseId, CorrectExpenseRequest request, CancellationToken ct);
}

public sealed record CreateCategoryRequest(string Name);

public sealed record ExpenseLineDto(string Description, decimal Quantity, decimal UnitPrice);

public sealed record RecordExpenseRequest(
    Guid CategoryId,
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    string? Note = null,
    IReadOnlyList<ExpenseLineDto>? Lines = null);

public sealed record CorrectExpenseRequest(
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    string? Note = null,
    IReadOnlyList<ExpenseLineDto>? Lines = null);

// Composition root extension (the only other public symbol).
public static IHostApplicationBuilder AddExpensesModule(this IHostApplicationBuilder builder);
```

Everything in Infrastructure (`ExpensesDbContext`, repositories, `OutboxMessage`, the facade impl's
collaborators) is `internal`. Create operations return the new aggregate's `Guid` directly — the method
name disambiguates; a named result record is noted in Open questions if that proves ambiguous.

## Packages (verify exact versions with `dotnet package search` before pinning)

Add `<PackageVersion>` entries to `Directory.Packages.props` — no inline versions:

- `Npgsql.EntityFrameworkCore.PostgreSQL` (EF Core 10 line)
- `Microsoft.EntityFrameworkCore.Design` (migrations)
- `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` (13.3.x train — Aspire client integration)
- `Testcontainers.PostgreSql` (test project only)

(`Microsoft.EntityFrameworkCore` comes transitively via the Npgsql provider; pin explicitly only if a
direct reference is needed.)

## Open questions

- **Boundary error model** — propagate `DomainException`, or return `Result<T>` from `IExpensesApi`?
  Settle with the add-expense form; affects how Blazor renders validation.
- **Migration application in production** — startup `MigrateAsync` vs. migration bundle. Deferred; dev uses
  startup migrate.
- **`xmin` concurrency token** — recommended (free, guards two-device edits). Confirm or skip for V1.
- **Create-result shape** — bare `Guid` vs. a named `record` (`RecordExpenseResult(Guid ExpenseId)`).
- **`AddExpensesModule` on `IHostApplicationBuilder`** — confirm the deviation from CLAUDE.md's
  `IServiceCollection` wording; I'll update CLAUDE.md to match.

## Verification

1. `dotnet restore` — clean, no NU1507.
2. `dotnet build` — 0 warnings, 0 errors (warnings-as-errors on).
3. `dotnet test tests/Modules/Expenses/Roamint.Expenses.Infrastructure.Tests` — green against a
   Testcontainers Postgres. Cases:
   - `RecordExpense` round-trips: aggregate + owned lines persist; `amount`/`currency`/typed-id columns
     correct; re-reading reconstructs equal `Money`/`Currency`.
   - Saving an aggregate writes its domain events to `outbox_messages` **in the same transaction** (rollback
     → no rows; commit → matching rows with the right discriminator), and the aggregate's `DomainEvents` is
     cleared after save.
   - `Category` archive/rename persist `Status`/`Name`; archived round-trips as `Archived`.
   - `xmin` concurrency: a stale-write conflict throws `DbUpdateConcurrencyException` (if the token is kept).
4. `dotnet test` (full) — existing Domain/Application/dispatcher suites stay green, including the new
   "`AddApplicationDispatcher` called twice → one dispatcher registration" case.
5. Initial migration generates and applies cleanly to an empty database.
6. `dotnet run --project src/Hosting/Roamint.AppHost --launch-profile http` — host boots, dashboard goes
   green, `expenses` schema + tables exist; resolving `IExpensesApi` and calling `RecordExpenseAsync`
   persists an expense and writes an outbox row.

## Outcome (what actually shipped vs. the plan)

- **`xmin` concurrency deferred.** Npgsql 10 removed `UseXminAsConcurrencyToken`, and the manual
  `uint`/"xmin" mapping scaffolds an *invalid real column* (Postgres reserves `xmin`). Rather than ship a
  fragile annotation hack, concurrency is left for a focused follow-up (single-user V1; revisit with the
  add-expense form / phone-concurrency path). Still listed in Open questions.
- **`IHasDomainEvents` added to the kernel.** The `SaveChanges` capture override needs to read events off
  any aggregate without knowing its `TId`; `AggregateRoot<TId>` now implements a small non-generic
  interface. (A second, expected Shared touch beyond the `TryAdd` tweak.)
- **`ExpenseLine.UnitPrice` uses a single-column `MoneyConverter`,** not a complex mapping — EF's
  `ComplexProperty` isn't available on owned-collection builders. `Expense.Amount` keeps the two-column
  complex mapping (it's what the summary reads will group by). Lines are never queried by price, so the
  packed `"amount|CODE"` column is acceptable.
- **EF versions pinned** (`Microsoft.EntityFrameworkCore` + `.Relational` at 10.0.8) to stop the Npgsql
  provider and Design tooling dragging in mismatched 10.0.x assemblies (MSB3277). `dotnet-ef` is a local
  tool in `.config/dotnet-tools.json`. Scaffolded migrations carry a folder-scoped `.editorconfig`
  (`generated_code = true`) so warnings-as-errors doesn't fail on generated style.
- **Verification done deterministically, not via a manual Aspire boot.** `Roamint.Expenses.Contracts.Tests`
  boots `AddExpensesModule` against Testcontainers using the `roamintdb` connection name and the dev
  migration hosted service, then drives `IExpensesApi` end-to-end (persist + read-back via the repo port,
  and `DomainException` propagation for archived-category / unknown-currency). 60 tests total, green.

## What's next

The **add-expense Blazor form** (V1 roadmap item 2) consuming `IExpensesApi` — mobile-first, one-handed
input. Later, in dependency order: the **read models** (Dapper: expense list, monthly summary) and the
**outbox drain** (poll + domain→integration translation + dispatch), which lands when the **Wallet** module
gives the events a consumer.
