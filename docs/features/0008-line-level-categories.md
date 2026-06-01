# 0008 — Expenses module: line-level categories + quantities

**Status:** Done — automated verification green (Domain 39 · Application 15 · Infrastructure 13 · Contracts 6); manual phone-over-hotspot pass on `/add` still pending
**Supersedes parts of:** `0005` (schema: category moves off the expense), `0006` (form: category is per-line), `0007` (line shape: `Amount` not `UnitPrice × Qty`; category suggestion is per-line)
**Lands in (planned):**
`src/Modules/Expenses/Roamint.Expenses.Domain/` (aggregate reshape),
`src/Modules/Expenses/Roamint.Expenses.Application/` (command/draft shape, suggestion queries),
`src/Modules/Expenses/Roamint.Expenses.Infrastructure/` (re-scaffolded migration, read models),
`src/Modules/Expenses/Roamint.Expenses.Contracts/` (DTO reshape),
`src/Bootstrap/Roamint.Web/Components/Pages/AddExpense.razor(.css)` (line-oriented entry),
`tests/Modules/Expenses/...`

## Context

The model built through `0005`–`0007` puts **one category on the expense** and treats line items as
an optional itemised breakdown (`UnitPrice × Quantity`). That contradicts how the user actually
records spending, and how they need to report on it.

A real shopping trip is one payment spanning several categories:

| Item    | Category  | Paid | Qty    |
|---------|-----------|------|--------|
| Apple   | Food      | 400  | 500 g  |
| Shampoo | Hygiene   | 1000 | 2 Pcs  |
| Knife   | Household | 900  | 1 Pcs  |

The categorized unit is the **item**, not the trip. With one category per expense, the headline
question — "how much on Food in December" — is unanswerable for any multi-category trip. The
expense-level category was the design error.

Two further facts from the user shape this slice:

- **Price is the amount paid for the item**, not a per-unit price. Quantity is never a multiplier.
- **Quantity is reportable**, not decorative: "how much Food (by weight) did I buy in December" is a
  target report. That only works if quantity is stored as structured `{ value, unit }` and units are
  canonical and non-convertible.

## Scope

In:

- Reshape `Expense` / `ExpenseLine`: category and paid-amount move to the line; quantity becomes a
  typed optional value object; the expense total is always derived.
- Re-scaffold the `expenses.` schema migration (no real data exists; clean redo).
- Pivot `/add` to line-oriented entry (item + category + paid amount + optional qty/unit per row).
- Repoint `0007` suggestions at the per-line flow.

Out (unchanged from roadmap):

- Quantity/weight **report screens** — this slice makes the data *correct and queryable*; the Dapper
  analytics + report UI still land with Wallet + monthly summary.
- Wallet integration, AI category inference (Insights, V2+), category management UI.

## Decisions

### D1 — Category and paid amount live on the line; the expense is the payment

`Expense` becomes a container for one payment: one currency, one date, ≥1 line, a derived total.
`ExpenseLine` carries `CategoryId` and `Amount` (what was paid for that item). A single-item buy
(coffee, taxi) is simply a one-line expense — no special case in the domain; the UI makes it feel
like one row.

Rationale beyond matching the user's flow: a **Receipt** (V2 OCR) is by definition one receipt → many
categorized lines, and **Wallet** deducts once per payment. Line-level category + trip-level payment
is the grain both future modules already want. The flat "each item is its own expense" alternative
was rejected because it splits one cash payment into N wallet deductions.

### D2 — `ExpenseLine.Amount` is the paid amount; quantity is not a multiplier

The `0007` `LineTotal = UnitPrice × Quantity` is dropped. A line stores `Amount` (the money paid for
that item) directly. `Quantity` is independent optional annotation. This is why quantity *can* be
optional: it never participates in the money arithmetic.

### D3 — The expense total is always derived

With ≥1 line mandatory and each line carrying its paid amount, `Total = Σ line.Amount` (in the
expense currency) — always, not the conditional dual-mode of `0007` D1. There is no path where the
user types a total directly. The line-sum invariant stops being something the UI can trip; it's just
the definition of the total.

### D4 — One currency per expense, inherited by lines

A trip is paid in one currency. `Expense.Currency` is derived from its lines and every line must
match it (the domain rejects a mismatched line). The segmented USD/AMD/USDT control sets it for the
whole expense; switching it re-rounds every line amount to the new minor-unit scale. Same rule as
`0006` D2 / `0007` D2, now applied to `line.Amount`.

### D4b — `ExpenseLine` is mapped as a regular child entity, not an owned collection

`0005`/`0007` mapped lines via `OwnsMany`, with the line price squeezed into a single `"amount|CODE"`
string column because "lines are never queried by price." That premise is now false: category spend
sums line amounts and weight reports sum line quantities, both in SQL. But `ComplexProperty`
(the two-column `Money` mapping) is unavailable on owned-collection builders (the documented EF trap),
so an owned line can't expose a numeric `amount` column without polluting the domain with backing
scalars. So `ExpenseLine` becomes a **regular child entity** of the `Expense` aggregate: its
`EntityTypeBuilder` supports `ComplexProperty(Amount)` (`amount` + `currency` columns, the proven
root-level pattern) and an optional `OwnsOne(Quantity)` (`quantity_value` + `quantity_unit`). The
aggregate boundary is unchanged — lines are created only through `Expense`, the repository `Include`s
them, and no `DbSet<ExpenseLine>` is exposed. `MoneyConverter` (the single-column workaround) is
deleted. This also requires `Quantity` to be a reference type — see D5.

### D5 — Quantity is a typed value object over a closed, canonical unit set

```
QuantityUnit { Grams, Milliliters, Pieces }   // codes: "g", "ml", "Pcs"
Quantity     { decimal Value, QuantityUnit Unit }   // Value > 0
```

Three units, one canonical unit per dimension (mass / volume / count). The user always records grams
(never kg) and millilitres (never L), so **no unit conversion ever happens** — aggregation is
`SUM(value) WHERE unit = 'g' GROUP BY category, month`, with mixed units reported side by side
("Food: 3500 g + 8 Pcs"). The closed enum is the user's explicit consistency requirement, not
gold-plating; it is deliberately small and only widens if a real need appears.

`Quantity` is **optional** per line (a taxi line has none). When present it is first-class, stored
data — `quantity_value` + `quantity_unit` columns — not a note. It is a `record` (reference type) so it
can map as an optional `OwnsOne` on the line (D4b); `quantity_unit` stores the canonical code string
(`"g"`/`"ml"`/`"Pcs"`) via a converter, with the `QuantityUnit ↔ code` mapping living on the domain
enum (one source of truth, mirroring `Currency.Code`).

### D6 — Event shape follows the payment, not the categories

`ExpenseRecorded` drops `categoryId` and carries `(ExpenseId, Money Total, DateOnly OccurredOn)` —
exactly what Wallet needs to deduct one payment. The per-category / per-quantity breakdown is an
Expenses-internal read concern (Dapper, later), not cross-module event data. `ExpenseRecategorized`
is removed; recategorizing now means editing a line, which flows through `Correct`.

### D7 — `0007` suggestions move per-line and also remember the unit

`SuggestItems` returning `ItemSuggestion(Text, CategoryId)` was already built for "pick an item, light
its category chip" — that now fills a **line's** category in one round trip, which is precisely what
per-item categorization wants. It also carries the **quantity unit**: `ItemSuggestion(Text, CategoryId,
Unit?)`, sourced from the most-recent line with that item name, so picking "cheese" pre-selects grams
along with its category. `SuggestCategory` (free-text-blur path) keys off the line's item text.
The autocomplete corpus shrinks to `expense_lines.description` (note is no longer the item-bearing
field); `note` stays an optional expense-level field but is excluded from the item corpus.

Tags are **not** part of this slice's suggestion payload; `ItemSuggestion` grows a `TagIds` field in
`0009` when the `Tag` aggregate exists.

## Domain model

```
Expense (aggregate root)
  Id          : ExpenseId
  OccurredOn  : DateOnly
  Currency    : Currency                       // derived from lines; all lines must match
  Total       : Money                          // derived = Σ line.Amount
  Note        : string?                        // optional, expense-level
  Lines       : IReadOnlyList<ExpenseLine>     // ≥1 required

ExpenseLine (owned entity)
  Id          : ExpenseLineId
  Description : string                          // the item; required, bounded
  CategoryId  : CategoryId                      // required
  Amount      : Money                           // paid amount; required, positive
  Quantity    : Quantity?                       // optional { Value>0, Unit ∈ {g, ml, Pcs} }
```

**Invariants**

- Expense has ≥1 line.
- Every `line.Amount.Currency == Expense.Currency`.
- `Total == Σ line.Amount`.
- Line: `Description` non-empty and ≤ max length; `Amount` positive; if `Quantity` present, `Value > 0`.
- `OccurredOn` not in the future (unchanged).

**Drafts / factory**

```
ExpenseLineDraft(string Description, CategoryId CategoryId, Money Amount, Quantity? Quantity)
Expense.Record(ExpenseId id, DateOnly occurredOn, IEnumerable<ExpenseLineDraft> lines,
               TimeProvider clock, string? note = null)
Expense.Correct(DateOnly occurredOn, IEnumerable<ExpenseLineDraft> lines,
                TimeProvider clock, string? note = null)
```

`Expense.CategoryId`, `Expense.Recategorize`, `ExpenseLine.UnitPrice`, `ExpenseLine.LineTotal`, and
the `amount`-as-input parameter are removed.

**Events**

- `ExpenseRecorded(ExpenseId Id, Money Total, DateOnly OccurredOn)`
- `ExpenseCorrected(ExpenseId Id, Money Total, DateOnly OccurredOn)`
- `ExpenseRecategorized` — removed.

## Public surface

- **Contracts (reshaped):**
  - `RecordExpenseRequest` — drops top-level `CategoryId`/`Amount`; `Lines` becomes required, and
    `ExpenseLineDto` gains `CategoryId` + `Amount` + optional `Quantity { decimal Value, string Unit }`,
    drops `UnitPrice`/`Quantity`-as-decimal.
  - `IExpensesQueries.SuggestItemsAsync(prefix, limit)` → `ItemSuggestion(string Text, Guid? CategoryId,
    string? Unit)` — corpus is now line descriptions only; payload adds the remembered unit.
  - `IExpensesQueries.SuggestCategoryAsync(itemText)` — unchanged signature.
- **Schema (re-scaffolded migration):** `expenses.expenses` drops `category_id`; `expenses.expense_lines`
  gains `category_id` (FK → `expenses.categories`, intra-schema, allowed), `amount`, `quantity_value`,
  `quantity_unit`; renames/removes `unit_price`.
- **Host:** `AddExpense.razor` becomes line-oriented (per-row item + category chip + paid amount +
  optional qty/unit); derived total shown read-only. Still references Contracts only.

## Open questions

- **Unit storage form:** store `quantity_unit` as the string code (`"g"`/`"ml"`/`"Pcs"`) for
  readable Dapper analytics, or as the enum int? Leaning string code — analytics queries read better
  and the set is tiny/stable. Confirm.
- **Per-row category UI on a phone:** chips-per-line could get tall fast. Likely a compact per-line
  category picker (chip row collapses to the chosen chip + a "change" affordance). Detailed in the
  `/add` rework, not blocking the domain.

## Verification

- **Domain (unit):** ≥1-line requirement; total = Σ line amounts; mismatched line currency rejected;
  optional quantity with `Value > 0` and each unit; future-date rejection still holds.
- **Integration (Infrastructure, Testcontainers Postgres):** round-trip an expense with 3 lines
  across 3 categories + mixed quantities; assert columns persist and `category_id` FK resolves.
  `SuggestItems`/`SuggestCategory` over the line-only corpus.
- **Module (Contracts):** `RecordExpenseAsync` with the reshaped request maps drafts correctly via
  `AddExpensesModule` wiring.
- **Manual (the real flow):** `/add` on laptop + phone-over-hotspot — enter the apple/shampoo/knife
  trip, confirm three categories persist on one expense, total derives, quantities store with units.
- **Build gate:** `dotnet build` clean under warnings-as-errors.
