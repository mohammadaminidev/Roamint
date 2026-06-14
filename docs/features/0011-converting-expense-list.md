# 0011 — Converting expense list + exchange-rate page

**Status:** Proposed (design — awaiting approval before the Expenses read-model code)
**Lands in (proposed):** `src/Modules/Expenses/Roamint.Expenses.Application/`,
`src/Modules/Expenses/Roamint.Expenses.Infrastructure/`,
`src/Modules/Expenses/Roamint.Expenses.Contracts/`,
`tests/Modules/Expenses/...`, `src/Bootstrap/Roamint.Web/`

## Context

Everything records expenses but nothing shows them back. This slice delivers the **expense list** — the
payoff screen — with the display-denomination conversion decided across 0010 (latest-known rate, fed by
exchanges + manual override, USD-spoke triangulation). It composes Expenses rows with Wallet rates
**host-side** (0010 D5), and adds the **exchange-rate page** that the missing-rate alert routes to (0010 D5a).

Two parts:

1. **Expenses read model (gated)** — there is no "list expenses" query yet (`IExpensesQueries` has only
   categories/tags/suggestions). Add `ListExpenses`.
2. **Web UI (not gated)** — the list page, the host converter, and the rate page.

## Decisions

### D1 — `ListExpenses` read model (Expenses module)

- New query `ListExpenses(DateOnly? From, DateOnly? To, Guid? CategoryFilter)` → `IReadOnlyList<ExpenseListRow>`,
  handled by an `AsNoTracking` read model in Infrastructure (the established pattern). Newest-first by
  `OccurredOn` then id.
- **Currency is *not* a filter** (0010 D5a — it became a display denomination). The query filters by date
  range and category only. Category match is **line-level**: an expense matches if *any* line is in the
  category (`expense.Lines.Any(...)` — translates fine, per the 0007 EF note).
- Row shape carries what the screen and converter need:
  ```
  ExpenseListRow(Guid Id, DateOnly OccurredOn, string CurrencyCode, decimal Total, string? Note,
                 IReadOnlyList<ExpenseLineRow> Lines)
  ExpenseLineRow(string Description, Guid CategoryId, string CategoryName, decimal Amount,
                 decimal? QuantityValue, string? QuantityUnit)
  ```
  `CategoryName` is joined in (categories share the schema). `Total`/`CurrencyCode` are the expense's
  original money — never converted in the module (0002 "no FX in the domain" holds). The header category
  label is derived UI-side (single category name, or "Mixed" when lines differ).
- **Tags are omitted from list rows in this slice.** The mockup shows tag pills, but tag-based display and
  reporting are parked with the monthly summary (roadmap). Adding the join now is scope the screen doesn't
  need to be useful. Revisit with reports.

### D2 — Host-side converter (Web, not a module type)

- A small `RateConverter` UI helper in `Roamint.Web`, built from `IWalletQueries.GetCurrentRatesAsync()`
  plus a target `Currency`. Algorithm (0010 D3): identity → 1; stored pair (either direction) → rate or
  reciprocal; else triangulate through USD. Rounds the converted value to the **target** currency's minor
  units (`Money.Of`).
- `TryConvert(decimal amount, Currency from)` returns a result distinguishing **converted** from
  **no-rate-for-`from`** (0010 D5a) so the list can name the missing currency and route to the rate page —
  never a silent best-effort number.

### D3 — The list screen (`/expenses`)

- Currency control = **display denomination**: `All` shows each row in its **original** currency with
  per-currency day subtotals (no conversion); a specific currency converts **every** row into it.
- **Missing rate ⇒ alert + route**, not silent fallback. When a chosen denomination can't reach some
  visible row's currency, show a banner naming the pair(s) with a link to `/rates`; once entered, the list
  re-converts.
- Grouped by day, newest first; each row expands to its lines (description · quantity · category · amount),
  matching the approved mockup. Day subtotal in the selected denomination (or per-currency under `All`).
- Rounding (0010 open item, settled here): convert each row's total then display; **day subtotal converts
  each currency's day-subtotal, not a sum of already-rounded rows**, so the total stays internally honest.

### D4 — The exchange-rate page (`/rates`)

- Lists current rates (`IWalletQueries.GetCurrentRatesAsync`) and offers **manual entry** ("how many
  `Quote` per 1 USD" — 0010 D3/Q2) via `IWalletApi.SetRateAsync`, plus **record an exchange** (the swap
  form) via `IWalletApi.RecordExchangeAsync`. It is both the manual-override UI and the missing-rate
  redirect target.

### D5 — Navigation

- Add the bottom-nav shell from the mockup to `MainLayout` (Expenses · Add · Rates), so the screens are
  reachable. Minimal — just the tabs these slices need; not a full nav system.

## Public surface (additions)

`IExpensesQueries` gains:
```csharp
Task<IReadOnlyList<ExpenseListItem>> ListExpensesAsync(
    DateOnly? from = null, DateOnly? to = null, Guid? categoryFilter = null, CancellationToken ct = default);
```
with primitive DTOs `ExpenseListItem` / `ExpenseLineItem` mirroring the row shapes above. No write-API
change. Wallet's surface is unchanged (already shipped in 0010).

## Decided (2026-06-13)

- **Default date range = current month**, with a range control to change it.
- **Dedicated `/expenses` route; `/` redirects to it for now.** Home later becomes a reports dashboard
  (monthly summary etc.), with the full list staying at `/expenses`.
- **`/rates` includes both** manual "per 1 USD" entry and recording an exchange (the swap is the primary
  rate source).

## Verification

1. `dotnet build` / full `dotnet test` green (warnings-as-errors).
2. **Expenses read-model tests** (Testcontainers): `ListExpenses` returns newest-first; date-range and
   line-level category filters; `CategoryName` joined; lines present for expansion.
3. **Converter tests** (Web): identity, direct, reciprocal, USD-triangulated, and missing-rate paths;
   rounding to target minor units.
4. **Manual run** (Aspire): record a few expenses in mixed currencies + an exchange; `/expenses` shows
   them under `All` in originals, converts under a chosen currency, and a missing rate routes to `/rates`;
   entering a rate there makes the list convert.

## Implementation order (reviewable steps)

1. **Expenses `ListExpenses` read model** — ✅ done. Query + `ExpenseRowView`/`ExpenseLineRowView`
   (Application), `ListExpensesHandler` (`AsNoTracking`, category-name join, line-level `Any` filter,
   newest-first), `IExpensesQueries.ListExpensesAsync` + DTOs. 2 Testcontainers tests.
2. **Host converter** — ✅ done. `Roamint.Web/Services/RateConverter.cs` (identity / direct / reciprocal /
   USD-triangulate, `TryConvert` reports missing). New `Roamint.Web.Tests` project, 6 tests.
3. **`/expenses` list screen** — ✅ done. `ExpenseList.razor` — denomination control ("All" = originals),
   category chips, current-month date range, day grouping + per-currency or converted subtotals,
   expand-to-lines, missing-rate banner → `/rates`.
4. **`/rates` page + nav** — ✅ done. `Rates.razor` (current rates, manual "per 1 USD" `SetRate`,
   record-exchange), shared `NavBar`, and `/` redirects to `/expenses`.

**Status: code complete, 155 tests green. Not yet visually verified in a running browser** — the Blazor
pages build and their logic is unit-tested, but the rendered layout/interaction needs an Aspire run.

## What's next

After 0011: **Wallet balances** (per-currency wallets applying exchanges + `ExpenseRecorded` — the slice
that builds the **outbox drain**), then tag-based **report screens** with the monthly summary.
