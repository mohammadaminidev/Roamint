# 0004 — Expenses module: application layer

**Status:** Done
**Lands in:** `src/Modules/Expenses/Roamint.Expenses.Application/`, `tests/Modules/Expenses/Roamint.Expenses.Application.Tests/`, `Roamint.slnx`

## Context

The domain (0002) can express and guard expenses and categories; the dispatcher (0003) provides the
command/query/event seam. This slice wires the two together: the **write-side use cases** for the
Expenses module as command handlers riding the dispatcher, plus the **repository ports** the handlers
depend on. It is the last piece before persistence — EF Core, the `expenses.` schema, and the
transactional outbox arrive in the next slice and *implement* the ports defined here.

Keeping this slice infrastructure-free means every use case is exercised with fast in-memory fakes and
`FakeTimeProvider`, the same discipline as 0002/0003. It also forces the layering question — what the
Application owns versus the Domain versus the future Infrastructure — to be answered explicitly rather
than by accident.

## Decisions

- **Application-only slice; no EF, no outbox, no host wiring.** Repositories and the unit of work are
  *interfaces* (driven ports). Their only implementations in this slice are hand-rolled in-memory fakes
  living in the test project. Real EF-backed implementations and the module's `Contracts` wiring are the
  next slice.
- **Repository interfaces live in `Application`, not `Domain`.** They are driven ports of the
  application's use cases (the handlers depend on them; Infrastructure implements them). Putting them in
  the kernel/Domain would pull a persistence-shaped abstraction into a layer CLAUDE.md mandates stays
  infrastructure-free.
- **`IUnitOfWork.SaveChangesAsync` is the commit seam, kept separate from the repositories.** Handlers
  load/create an aggregate, mutate it, then call `SaveChangesAsync` once. The future transactional
  outbox hooks into exactly this call (persist aggregate + outbox rows in one transaction), so the seam
  earns its place now rather than being a speculative split.
- **Handlers orchestrate; the one genuine application rule is cross-aggregate.** Amount positivity,
  "not in the future", line-sum equality, and name length are *domain* invariants already enforced by the
  aggregates — handlers do not re-check them. The rule that cannot live inside an aggregate is
  **"an expense may only reference a category that exists and is `Active`"** (the `Expense` aggregate has
  no access to `Category` state). `RecordExpense` and `RecategorizeExpense` enforce it by loading the
  target category. This is what justifies an Application layer here beyond mere mapping.
- **No validator framework / pipeline behavior yet.** 0003 deferred `IPipelineBehavior` until a second
  cross-cutting concern appears, and that still holds. Input handling in this slice is (a) mapping —
  `Currency.FromCode` turning the command's string code into a `Currency`, which already throws
  `DomainException` on an unknown code — and (b) the cross-aggregate guard above. Both are done inline in
  the handler. Introducing an `IValidator<T>` abstraction with no pipeline to run it generically would be
  a premature seam (one method dressed as an interface). Revisit when logging/timing or transaction
  behaviors give the pipeline a real second job.
- **Error model.** Business-rule violations reuse the kernel's `DomainException` (e.g. "category is
  archived and cannot accept expenses"). "Aggregate not found" is an orchestration outcome, not an
  aggregate invariant, so the module adds two thin exceptions — `CategoryNotFoundException` and
  `ExpenseNotFoundException` (both `: DomainException`, carrying the offending id). They are thrown
  *synchronously* from the handler before any mutation, consistent with the dispatcher's "misuse throws
  at the call site" stance.
- **Commands use strongly-typed IDs** (`CategoryId`, `ExpenseId`) — module-internal idiom. How the Blazor
  host ultimately issues these (a `Contracts` facade translating primitive DTOs → commands, versus
  exposing the commands publicly) is a `Contracts`-slice decision and is deliberately left open here.
- **`TimeProvider` injected into every handler** that mints an id or calls a domain factory
  (`ExpenseId.New(clock)`, `Expense.Record(..., clock, ...)`). Registered once as `TimeProvider.System`
  in the host later; tests pass `FakeTimeProvider`.
- **Tests use hand-rolled in-memory fake repositories + `FakeTimeProvider`, no NSubstitute.** Dictionary-
  backed fakes read more naturally for these assertions, double as a reusable test support, and keep the
  package set lean (consistent with 0003's hand-written fakes). NSubstitute stays unused until a slice
  genuinely needs interaction verification a fake can't express.

## Domain model

No new domain types. This slice consumes the 0002 aggregates (`Category`, `Expense`, `ExpenseLine`,
`ExpenseLineDraft`) and the kernel's `Money`/`Currency`/`DomainException`.

## Public surface

All in namespace `Roamint.Expenses.Application` (consumed by the future Infrastructure/Contracts slices
and the test project; nothing reaches the web host yet).

```csharp
// ---- Commands: Category ----
public sealed record CreateCategory(string Name) : ICommand<CategoryId>;
public sealed record RenameCategory(CategoryId CategoryId, string NewName) : ICommand;
public sealed record ArchiveCategory(CategoryId CategoryId) : ICommand;

// ---- Commands: Expense ----
public sealed record ExpenseLineInput(string Description, decimal Quantity, decimal UnitPrice);

public sealed record RecordExpense(
    CategoryId CategoryId,
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    string? Note = null,
    IReadOnlyList<ExpenseLineInput>? Lines = null) : ICommand<ExpenseId>;

public sealed record RecategorizeExpense(ExpenseId ExpenseId, CategoryId CategoryId) : ICommand;

public sealed record CorrectExpense(
    ExpenseId ExpenseId,
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    string? Note = null,
    IReadOnlyList<ExpenseLineInput>? Lines = null) : ICommand;

// ---- Repository ports ----
public interface ICategoryRepository
{
    Task<Category?> GetAsync(CategoryId id, CancellationToken ct);
    Task AddAsync(Category category, CancellationToken ct);
}

public interface IExpenseRepository
{
    Task<Expense?> GetAsync(ExpenseId id, CancellationToken ct);
    Task AddAsync(Expense expense, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}

// ---- Errors ----
public sealed class CategoryNotFoundException(CategoryId id) : DomainException(/* … */);
public sealed class ExpenseNotFoundException(ExpenseId id) : DomainException(/* … */);

// Handlers are `internal sealed`, discovered by AddApplicationDispatcher's assembly scan.
```

### Handler flows (the substance)

- **`CreateCategory`** → `id = CategoryId.New(clock)`; `Category.Create(id, Name)`; `AddAsync`;
  `SaveChangesAsync`; return `id`.
- **`RenameCategory` / `ArchiveCategory`** → load category or throw `CategoryNotFoundException`; call
  `Rename`/`Archive` (both idempotent in the domain); `SaveChangesAsync`.
- **`RecordExpense`** → `currency = Currency.FromCode(CurrencyCode)`; load category or throw
  `CategoryNotFoundException`; if `Archived` throw `DomainException`; build `Money.Of(Amount, currency)`
  and map `Lines` → `ExpenseLineDraft` (unit price = `Money.Of(line.UnitPrice, currency)`);
  `Expense.Record(ExpenseId.New(clock), …, clock, …)`; `AddAsync`; `SaveChangesAsync`; return the id.
- **`RecategorizeExpense`** → load expense or throw `ExpenseNotFoundException`; load target category or
  throw `CategoryNotFoundException`; if `Archived` throw `DomainException`; `expense.Recategorize`;
  `SaveChangesAsync`.
- **`CorrectExpense`** → load expense or throw `ExpenseNotFoundException`; map currency/amount/lines as in
  `RecordExpense`; `expense.Correct(…, clock, …)`; `SaveChangesAsync`.

## Open questions

- **Host invocation shape** — `Contracts` facade with primitive DTOs vs. public commands. Decided in the
  Contracts slice; affects whether commands keep strongly-typed IDs at the boundary.
- **Archived-category policy on `Correct`** — recording and recategorizing block archived categories, but
  `Correct` keeps an expense on its existing category, so it is unaffected. If a future edit lets `Correct`
  change the category, fold the same guard in.
- **Validator/pipeline** — still deferred (see Decisions). The trigger is a second cross-cutting concern.

## Verification

1. `dotnet restore` — clean, no NU1507.
2. `dotnet build` — 0 warnings, 0 errors (warnings-as-errors on).
3. `dotnet test tests/Modules/Expenses/Roamint.Expenses.Application.Tests` — all green. Cases:
   - `CreateCategory` persists and returns the new id; the aggregate carries `CategoryCreated`.
   - `RenameCategory`/`ArchiveCategory` on a missing id throw `CategoryNotFoundException`.
   - `RecordExpense` persists, returns the id, and `SaveChangesAsync` is called once.
   - `RecordExpense` against a missing category throws `CategoryNotFoundException`; against an archived
     category throws `DomainException`; an unknown `CurrencyCode` surfaces `DomainException`.
   - `RecordExpense` with line items whose totals don't match the amount surfaces the domain's
     `DomainException` (handler maps, domain guards).
   - `RecategorizeExpense` to an archived/missing category throws; happy path raises `ExpenseRecategorized`.
   - `CorrectExpense` on a missing expense throws `ExpenseNotFoundException`; happy path updates state.
4. `Roamint.Expenses.Application` references only `Roamint.Expenses.Domain` and
   `Roamint.Application.Abstractions`; the web host builds untouched.

## What's next

Slice C — Expenses **Infrastructure + Contracts**: EF Core mappings for the `expenses.` schema,
Dapper-free for now, concrete `ICategoryRepository`/`IExpenseRepository`/`IUnitOfWork`, the transactional
outbox that turns the buffered domain events into integration events, and the module's
`AddExpensesModule` wiring exposed from `Contracts`. That slice closes V1 roadmap item 1 and unblocks the
add-expense form.
