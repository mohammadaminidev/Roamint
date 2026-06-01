# 0007 — Expenses module: line items, item autocomplete, category suggestion

**Status:** Done — automated verification green; manual phone-over-hotspot pass still pending
**Lands in:**
`src/Modules/Expenses/Roamint.Expenses.Application/` (two read queries + projections),
`src/Modules/Expenses/Roamint.Expenses.Infrastructure/` (two EF read-model handlers),
`src/Modules/Expenses/Roamint.Expenses.Contracts/` (`IExpensesQueries` additions + DTOs),
`src/Bootstrap/Roamint.Web/Components/Pages/AddExpense.razor(.css)` (line-item entry, autocomplete, chip auto-select),
`tests/Modules/Expenses/...Infrastructure.Tests` + `...Contracts.Tests` (handler + wiring tests)

## Context

`0006` shipped the core add-expense form (amount, currency, category chips, date, note → one
`RecordExpenseAsync`) and cut steps 3–5 of README V1 item 2 to this slice. Those three steps are
the "richer entry" half of the daily-use flow:

3. **Line items** — optional itemised entry (description / qty / unit price).
4. **Item autocomplete** — suggest descriptions from prior note + line history.
5. **Category suggestion** — pre-select the most likely chip for the typed item, rule-based.

The domain and command/contract plumbing for lines is **already done** (built in `0002`/`0004`/`0005`):
`ExpenseLineDraft` → `ExpenseLine`, the line-sum invariant in `Expense.Record`, `ExpenseLineInput`
on `RecordExpense`, and `ExpenseLineDto` on `RecordExpenseRequest`. **This slice writes no domain
code.** It is two read queries plus the UI that drives them — extending the same EF `AsNoTracking`
read seam `0006` introduced, with no Dapper pulled forward.

This is the flow the user exercises daily on a phone over hotspot ([[phone-input-deployment]]), so
read chatter and round-trip latency are first-class design concerns here, not afterthoughts.

## Scope

In:

- `IExpensesQueries.SuggestItemsAsync(prefix, limit)` — autocomplete corpus from note + line history.
- `IExpensesQueries.SuggestCategoryAsync(itemText)` — rule-based category guess for typed item text.
- `AddExpense.razor`: expandable line-item section; amount derives from lines when lines exist;
  autocomplete dropdown on the item/line-description inputs; chip auto-select from the suggestion.

Out (unchanged from roadmap):

- Dapper read models / analytics (lands with Wallet + monthly summary).
- AI/embedding category inference (Insights, V2+). Suggestion here is a **heuristic over history**.
- Category management UI (README V1 item 5).
- Editing / correcting an existing expense from the UI (`CorrectExpense` exists; no screen yet).

## Decisions

### D1 — When lines exist, the amount is **derived and read-only**

The line-sum invariant is a hard domain rule: if any line is present,
`Σ round(qtyᵢ × unitPriceᵢ) == amount` exactly, in the expense currency
(`Expense.BuildLines`). Rather than let the user type an amount *and* lines and then bounce a
`DomainException` on mismatch, the form computes the amount from the lines whenever ≥1 line exists
and locks the amount input. With zero lines the amount is entered directly — **`0006` behaviour is
untouched** for the common cash-expense case.

The UI computes the running total with the *same* arithmetic the domain uses
(`Money.Of(unitPrice × qty)` per line, rounded to the currency's minor units, then summed), so the
derived amount the user sees is byte-identical to what the aggregate will re-derive. The line-sum
invariant becomes a backstop that the UI structurally cannot trip, not a validation path the user
fights.

### D2 — Currency: lines inherit the expense currency

The segmented USD/AMD/USDT control still sets one currency for the whole expense. Line unit prices
are entered and shown in that currency; there is no per-line currency. Switching currency
re-rounds every line's unit price to the new minor-unit scale (and therefore re-derives the amount),
mirroring how `0006` re-validates the single amount on currency switch. This matches the domain,
which rejects a line whose currency differs from the expense.

### D3 — Autocomplete corpus = distinct notes ∪ distinct line descriptions

Per `0006` resolved decision #1, "item" text is the note in the simple case and the line
description when lines are present. `SuggestItems` reads **both** `expenses.note` and
`expense_lines.description`, unions them, de-dupes case-insensitively, ranks **prefix-match before
contains-match** then by recency (most-recently-used first), and caps at a small `limit` (UI passes
6). The dropdown drives both the note field (simple mode) and each line-description field (line
mode).

### D4 — Category suggestion is rule-based, driven by the item text, and never overrides a manual pick

`SuggestCategory(itemText)` finds prior expenses whose **note or any line description** matches
`itemText` (case-insensitive, prefix-then-contains), and returns the **most-recently-used** category
among the matches (ties broken by frequency), or `null` when there is no signal — exactly the rule
named in `0006`. It returns the category id (the chip to light up), not a name.

In the UI the suggestion only moves the chip selection while the user has **not** manually tapped a
chip. Once the user taps, their choice is sticky and later suggestions are ignored for that entry.
`null` leaves the current selection alone. This keeps the feature helpful without ever fighting the
user — the daily failure mode we most want to avoid on a phone.

### D5 — One round trip per item: `SuggestItems` returns text **and** its category

`0006` sketched two separate queries (`SuggestItemDescriptions`, `SuggestCategory`). Over hotspot,
two trips per keystroke-batch is the wrong default. So `SuggestItems` returns
`(string Text, Guid? CategoryId)` per suggestion: picking a suggestion fills the text *and* yields
the category to auto-select in the same payload, no second call. `SuggestCategoryAsync` remains as a
**separate** method for the case where the user types free text and commits it **without** picking
from the dropdown (note blur / line-description blur) — there we have no pre-fetched category and
must ask. Two methods, but the hot path (pick-from-list) is one trip. *(This is the main departure
from the `0006` sketch — flagged as an open question below.)*

### D6 — Debounce + min-prefix to bound read chatter

Blazor Server turns every keystroke into a server round trip. Autocomplete fires only at **≥2
chars** and is **debounced ~250 ms** (timer reset per keystroke, cancel in-flight on dispose).
This is a UI concern living entirely in the host; the query handlers stay dumb. Min-prefix also
keeps the `contains` scan off pathologically broad terms.

### D7 — EF `AsNoTracking`, no Dapper; tables are personal-scale

Both handlers live in Infrastructure (read models that bypass the aggregate), registered through the
existing `AddApplicationDispatcher` Infrastructure scan — same pattern as `ListActiveCategoriesHandler`.
They touch two tables (`expenses`, `expense_lines`). At single-tenant personal volume this is fine
on EF; Dapper still waits for the analytics screens. The `contains`/`ILIKE` scans are unindexed for
now — acceptable at this scale, noted as a future trigram-index item, not built.

## Domain model

**No changes.** Lines, the line-sum invariant, line-currency match, positive qty/unit-price, and
note cleaning are all already enforced in `Expense` / `ExpenseLine` and merely *reflected* by the
UI. This slice is read-side + presentation over the existing aggregate.

## Public surface

- **New (Contracts):**
  - `IExpensesQueries.SuggestItemsAsync(string prefix, int limit, CancellationToken)` →
    `IReadOnlyList<ItemSuggestion>`.
  - `IExpensesQueries.SuggestCategoryAsync(string itemText, CancellationToken)` → `Guid?`.
  - `public sealed record ItemSuggestion(string Text, Guid? CategoryId);`
- **Internal (Application):** `SuggestItems(string Prefix, int Limit) : IQuery<IReadOnlyList<ItemSuggestionView>>`,
  `SuggestCategory(string ItemText) : IQuery<Guid?>`, and the `ItemSuggestionView(string Text, Guid? CategoryId)`
  projection.
- **Internal (Infrastructure):** `SuggestItemsHandler`, `SuggestCategoryHandler` (EF `AsNoTracking`).
- **Reused:** `IExpensesApi.RecordExpenseAsync` + `RecordExpenseRequest.Lines` (already carry lines).
- **Host:** `AddExpense.razor` gains the line section, autocomplete, and chip auto-select. Still
  references Contracts only.

## Resolved decisions (from review)

1. **D5 — merge.** `SuggestItems` returns `(Text, CategoryId)` so picking from the dropdown fills
   text and lights the chip in one round trip; `SuggestCategoryAsync` stays as the separate
   free-text-blur path. (Chosen over honouring the two-query `0006` sketch literally.)
2. **D4 scope — note + first line.** Simple mode: the note drives the category suggestion. Line
   mode: the **first** line's description drives it. Always non-overriding once the user taps a chip.
3. **Line-item visual — build in the mock's idiom.** Compact line rows (description + qty + unit
   price, per-row total, "+ add item", locked derived amount) styled to match the existing form. No
   separate prototype.

## Open questions

None blocking.

## Verification

- **Integration (Infrastructure, Testcontainers Postgres):**
  - `SuggestItemsHandler` — seed expenses with notes + lines; assert prefix-before-contains order,
    case-insensitive de-dupe across both columns, recency ranking, and `limit` cap.
  - `SuggestCategoryHandler` — seed history; assert most-recently-used category wins, frequency
    breaks ties, and no-match → `null`.
- **Module (Contracts):** both new `IExpensesQueries` methods round-trip through `AddExpensesModule`
  wiring (mirrors the existing `ExpensesQueriesTests`).
- **Manual (the real flow):** run the Aspire stack, open `/add` on laptop and phone-over-hotspot.
  - Add 2–3 line items; confirm the amount locks to the derived sum and persists with lines in the
    `expense_lines` table.
  - Type a known item; confirm the autocomplete dropdown and that picking it lights the right chip.
  - Type free text and blur; confirm the chip suggestion appears and that a manual chip tap is
    sticky thereafter.
  - Switch currency with lines present; confirm re-rounding and that submit still succeeds (no
    line-sum `DomainException`).
- **Build gate:** `dotnet build` clean under warnings-as-errors (watch `CA1848` on any handler
  logging — use the source generator or drop the log).
