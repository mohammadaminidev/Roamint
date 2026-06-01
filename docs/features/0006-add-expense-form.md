# 0006 — Expenses module: add-expense form (read side + Blazor UI)

**Status:** Approved — in progress (scope cut after step 2; steps 3–5 → `0007`)
**Lands in:**
`src/Modules/Expenses/Roamint.Expenses.Application/` (queries + handlers),
`src/Modules/Expenses/Roamint.Expenses.Contracts/` (`IExpensesQueries` + read DTOs),
`src/Modules/Expenses/Roamint.Expenses.Infrastructure/` (EF-backed read implementations),
`src/Bootstrap/Roamint.Web/Components/` (the form, layout, nav),
`tests/Modules/Expenses/...` (handler + query tests),
`docs/prototypes/0006-add-expense-mock.html` (reference mock, already present)

## Context

V1 roadmap item 1 (Expenses domain/application/infrastructure/contracts) is done; `IExpensesApi`
is the host's only write door. This slice closes **V1 item 2 — the add-expense form**: the first
real screen, mobile-first and one-handed, the flow the user actually exercises daily on a phone
over hotspot ([[phone-input-deployment]]). The visual target is the existing prototype at
`docs/prototypes/0006-add-expense-mock.html`.

Building the form surfaces the first **read** requirement in the module: the chips need a live
category list, and the full-scope form adds **item autocomplete** and **category suggestion**,
both of which read expense history. The module has had no read path until now (Dapper read models
were deferred). This slice introduces a thin, honest read seam — EF `AsNoTracking` query handlers
behind a new `IExpensesQueries` facade — **without** pulling the Dapper analytics layer forward.

## Scope

Full README item 2 splits into five separable sub-features. **This slice (0006) ships steps 1–2;
steps 3–5 are deferred to `0007`** (decided with the user — see Resolved decisions):

1. **Read seam + category list** *(0006)* — `IExpensesQueries.ListActiveCategoriesAsync`, EF-backed,
   through the query dispatcher. Chips render from real data.
2. **Core form** *(0006)* — amount, currency segmented control, category chips, date, note → one
   `RecordExpenseAsync` call. This alone satisfies the daily-use path.
3. **Line items** *(0007)* — optional expandable line entry (description / qty / unit price).
4. **Item autocomplete** *(0007)* — suggest descriptions from prior note/line history.
5. **Category suggestion** *(0007)* — pre-select the most likely chip for the typed item, rule-based.

`0006`'s only read query is `ListActiveCategories`. The suggestion/autocomplete queries
(`SuggestItemDescriptions`, `SuggestCategory`) and the `Lines` UI move with their steps to `0007`.

## Decisions

### A separate read facade, not reads on `IExpensesApi`

`IExpensesApi` is documented as the module's **write** API. Rather than dilute it, add a sibling
read interface (0006 surface; 0007 adds the suggestion methods):

```csharp
public interface IExpensesQueries
{
    Task<IReadOnlyList<CategoryListItem>> ListActiveCategoriesAsync(CancellationToken ct = default);
}

public sealed record CategoryListItem(Guid Id, string Name);
```

Both interfaces are registered by `AddExpensesModule`; the host depends only on Contracts, as
before. This keeps the CQRS seam visible at the public surface (write = `IExpensesApi`, read =
`IExpensesQueries`) and mirrors the command/query split already present in the dispatcher.

### Read handlers live in Infrastructure, EF `AsNoTracking`, dispatched, no Dapper

The dispatcher already has a query side (`IQuery<T>` / `IQueryHandler<,>` / `IQueryDispatcher`), and
`AddApplicationDispatcher` takes `params Assembly[]`. So the **query type lives in Application**
(`ListActiveCategories` + the `CategoryView` projection) while its **handler lives in
Infrastructure**, reading `ExpensesDbContext` with `AsNoTracking` and projecting to `CategoryView`.
This is the right home for a read model (it bypasses the aggregate and hits the projection
directly), and it avoids a pass-through "read store" port that would add an interface with no second
implementation — which the project's anti-gold-plating rule forbids. `AddExpensesModule` scans both
the Application **and** Infrastructure assemblies for handlers; the dispatcher registers
idempotently, so the host stays ignorant. No Dapper machinery is stood up before an analytics
screen needs it (that lands with Wallet / monthly summary). `IExpensesQueries` is a thin dispatcher
facade in Contracts, exactly like `ExpensesApi`, mapping `CategoryView` → `CategoryListItem`.

### Currencies offered: USD / AMD / USDT only

The segmented control offers the three **transactable** currencies. `IRR` stays display-only (it
exists in `Currency` for the future monthly-summary toman view) and is **not** selectable here.
Default selection: **AMD** (the dominant day-to-day cash currency), matching the mock.

### Amount input is currency-aware

`Currency.MinorUnits` drives parsing/rounding: AMD = 0 decimals, USD/USDT = 2. The form parses the
raw text against the selected currency's scale before building `RecordExpenseRequest`, so the
domain never sees an over-precise amount. Switching currency re-validates the entered amount.

### Date defaults to today, future blocked client-side

`OccurredOn` defaults to "today" from the injected `TimeProvider` (never `DateTime.UtcNow`). The
domain already rejects future dates; the form mirrors that as an input `max` and inline validation
so the user gets immediate feedback rather than a thrown `DomainException`.

### Category suggestion is rule-based, not AI

"Category suggestion" here is a **heuristic over history**, not embeddings/LLM — those belong to
the Insights module (V2+). Rule: find prior expenses whose line description or note matches the
typed item text (case-insensitive, prefix then contains), and return the **most recently used**
category among them (ties broken by frequency). Returns `null` when there is no signal, leaving the
user's current chip selection untouched. This keeps the feature inside the Expenses module with no
new dependencies.

### Domain errors surface as inline validation, not crashes

`RecordExpenseAsync` can throw `DomainException` subtypes (archived category, line-sum mismatch,
future date, non-positive amount). The form catches these at submit and renders the message against
the relevant field. Client-side checks pre-empt the common cases; the catch is the backstop so a
race (e.g. category archived between load and submit) degrades to a message, not an unhandled error.

### Render mode & layout

Interactive Server (already the host default). The page is a `@page "/add"` component (Home stays
the placeholder for now; nav links to it). Styling ports the mock's mobile-first CSS into the
component / a scoped stylesheet — max-width phone column, large tap targets, sticky submit.

## Domain model

No domain changes. This slice is read-side + UI over the existing `Expense` / `Category`
aggregates and the existing `RecordExpense` command. Line-sum, positive-amount, not-future, and
active-category invariants are already enforced in `Expense.Record` / the handler and are merely
*reflected* in the UI.

## Public surface

- **New (Contracts):** `IExpensesQueries` (one method), `CategoryListItem`. Registered in
  `AddExpensesModule`.
- **Reused (Contracts):** `IExpensesApi.RecordExpenseAsync`, `RecordExpenseRequest`.
- **Internal (Application):** `ListActiveCategories` query + `CategoryView` projection.
- **Internal (Infrastructure):** `ListActiveCategoriesHandler` (EF `AsNoTracking`); starter-category
  seeding in the dev migration service.
- **Host:** `/add` Blazor component (+ scoped stylesheet from the mock). Host still references
  Contracts only (and `DomainException` transitively from the shared kernel, the documented error
  contract).

## Resolved decisions (from review)

1. **"Item" text = the Note** for autocomplete/suggestion; line descriptions join the corpus when
   present. (Applies in `0007`.)
2. **Slice cut after step 2** — line items, autocomplete, suggestion move to `0007`.
3. **Empty-state:** dev-only seed of a small starter set (`Food, Transport, Rent, Groceries,
   Utilities`) in the migration service, guarded by "no categories yet". Real management is V1 item 5.
4. **Suggestion matching** (in `0007`): ship crude prefix-then-contains; revisit only if it annoys.

## Open questions

None blocking for `0006`.

## Verification

- **Integration (Infrastructure):** `ListActiveCategoriesHandler` against a Testcontainers Postgres —
  returns active categories name-ordered and excludes archived ones.
- **Module (Contracts):** `IExpensesQueries.ListActiveCategoriesAsync` round-trips through
  `AddExpensesModule` wiring and reflects a freshly created / archived category.
- **Manual (the real flow):** run the Aspire stack, open `/add` on the laptop and from the phone
  over hotspot, record an AMD expense with a note, confirm it persists (`expenses.` schema). Switch
  currency and confirm decimal handling. Attempt a future date / zero amount and confirm inline
  validation. Confirm the seeded starter categories appear as chips on a fresh DB.
- **Build gate:** `dotnet build` clean under warnings-as-errors (watch `CA1848` on any logging in
  handlers — use the source generator or drop the log).
