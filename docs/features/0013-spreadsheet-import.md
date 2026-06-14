# 0013 — Spreadsheet import (historical seed)

## Context

Before V2, the user wants to start using Roamint for real daily tracking. There is an existing
backlog of expenses in a Google Sheet (March–June 2026, ~850 item rows, all in AMD cash). This feature
is a **one-off, dev-only importer** that replays that history into the app through the existing domain
APIs so the monthly-summary home has real data from day one.

This is host-level tooling, not a module use case. It touches **no module internals** — it orchestrates
the public `Contracts` APIs (`IExpensesApi`, `IWalletApi`) exactly like the web host does. No new
aggregates, value objects, events, or schema.

### Source shape (observed from the sheet)

Live data is four columns, with **month-marker rows** (a lone month name in column A) splitting the list
into monthly sections; rows before the first marker are March.

| column   | meaning            | examples                                  |
|----------|--------------------|-------------------------------------------|
| `item`   | description (fa)   | سشوار, رب گوجه فرنگی                        |
| `type`   | category (fa)      | 11 distinct values (table below)          |
| `price`  | amount, AMD        | `4,990`, `790` (thousands comma)          |
| `number` | quantity           | `1`, `950 gr`, `500 ml`, `1000 gr`        |

A right-hand block (`دسته بندی / قیمت به درام / قیمت به تومان / قیمت به دلار` + rate cells) is a
**derived per-category rollup and the FX rates used**. It is *not* source data — Roamint computes FX from
the Wallet rate timeline — so the importer ignores it, except optionally lifting the rate cells into
`SetRate` (D7).

**Key gap: no dates.** Day was never recorded; the only time signal is the month section. Roamint's
`Expense` requires `OccurredOn`, so every imported expense is dated to a representative day of its month
(D3).

## Decisions

- **D1 — Replay through Contracts, don't bulk-insert.** The importer calls
  `IExpensesApi.RecordExpenseAsync` / `IWalletApi.SetRate`, getting full invariant checking, domain events,
  and the outbox for free, and proving the real write path against real data. It lives in `Roamint.Web`
  (e.g. `Import/SpreadsheetImporter.cs`) since the host already wires every module via Aspire. Bulk SQL was
  rejected: it bypasses validation, events, and the FX read path on *real* data.

- **D2 — CSV file at a gitignored path.** The user exports the tab to `seed/import.csv` (path
  configurable). Real data stays out of git per the privacy rule; only the importer code is committed.

- **D3 — Month granularity; date to the 1st.** Day is unrecoverable. Each expense is dated to the **1st of
  its month**, year defaulting to the current year (2026), configurable. Month markers (`April`, `May`,
  `June`) drive bucketing; the pre-marker section is March. The 1st keeps June in the past (today is the
  14th), satisfying the aggregate's no-future rule, and any in-month day folds into the same monthly-summary
  bucket regardless.

- **D4 — One Expense per (month × category).** ~9 categories × 4 months ≈ a few dozen tidy multi-line
  expenses, each line keeping its own item / amount / quantity, with `Note = "Imported · {Month} {Year}"`
  for traceability and easy later cleanup. Per-(month, category) totals equal exactly what the monthly
  summary reports. Rejected: one expense per row (≈850 single-line cards — noise, and invents nothing more
  truthful since the trip/day boundary is already lost).

- **D5 — Map categories to English; this becomes the real seed set.** The sheet's taxonomy is richer than
  the current placeholder seed (`Food/Transport/Rent/Groceries/Utilities`). We replace
  `ExpensesMigrationService.StarterCategories` with the mapped real set, and the importer looks categories
  up by name. Draft mapping:

  | Persian (`type`)          | English          |
  |---------------------------|------------------|
  | وسایل خونه                 | Household        |
  | خوراکی لول ۱/۲/۳            | Groceries (+ tier tags, D9) |
  | بهداشتی                    | Personal Care    |
  | غذا بیرون                  | Dining Out       |
  | حمل و نقل                  | Transport        |
  | سفر                       | Travel           |
  | متفرقه                    | Miscellaneous    |
  | مسکن                      | Rent             |
  | قبض                       | Utilities        |

- **D6 — Parsing rules.** Only three columns are used: `item` → description, `type` → category,
  `price` → amount (strip thousands commas → AMD). The sheet's `number` (quantity) column is **ignored** —
  imported lines carry no quantity. Currency is always AMD. The importer reads by header name, so any extra
  columns in the real export (the rollup block, a leftover `number`) are harmless.

- **D7 — Rates are an optional convenience, set once.** `ExchangeRate` is latest-wins, current-only (no
  historical timeline), and the home defaults to **AMD**, which needs no rate. To make the USD / IRT toggles
  work, set two current rates from the sheet's cells: `SetRate("AMD", <AMD per USD>)` and
  `SetRate("IRT", 145000)` (IRT per USD); the converter triangulates AMD→IRT through USD. Per-month
  historical rates are deliberately not modeled — the read model can't use them.

- **D8 — Dev-only, idempotent, explicit.** Guarded by `IHostEnvironment.IsDevelopment()`, triggered
  explicitly (not on every boot), and a no-op if any expenses already exist. Throwaway tooling — fine to
  delete once history is in.

- **D9 — Food tiers → Groceries + tier tags.** `خوراکی لول ۱/۲/۳` maps to a single `Groceries` category
  with tier **tags** `Essential` / `Comfort` / `Treat` (scoped to Groceries) applied per line. Reuses the
  existing tag system, keeps the category list clean, preserves the tier signal. The starter-tag seed in
  `ExpensesMigrationService` is updated to this tier set (replacing the anonymized placeholder tags).

- **D10 — Trigger via a dev-only `/import` page.** A guarded Blazor page (Development only) with an Import
  button that runs the importer and shows a result summary (categories ensured, expenses recorded per
  month, or "already imported — no-op"). Visible feedback and control; aligns with the future phone-input
  flow. No startup auto-run.

## Public surface

None. No `Contracts` change, no new DTOs or endpoints exposed to other modules. The only committed
artifacts are the importer in `Roamint.Web`, the updated starter-category/tag seed, and (minor) Tabler
icons for the new categories on the home screen.

## Open questions (minor — defaulting unless told otherwise)

1. **English category names** — proceeding with the D5 mapping (Household, Groceries, Personal Care,
   Dining Out, Transport, Travel, Miscellaneous, Rent, Utilities); easy to rename later.
2. **Year** — assuming all of March–June is **2026**.
3. **Rate values** — `IRT/USD = 145000` from the sheet; AMD/USD to be confirmed (sheet's `366` looks low
   vs. the real ~385–400). Rates are optional, so this doesn't block the import.

## Verification

- Export the tab → `seed/import.csv`. Run the importer in Development.
- Importer logs: N categories ensured, M expenses recorded across 4 months; re-running is a no-op.
- Home (`/`) in AMD shows non-empty totals for March–June with month stepping; per-category breakdown
  matches the sheet's right-hand rollup within rounding.
- After the optional `SetRate` calls, the USD and IRT toggles render without the "no rate" alert.
- `ExpenseList` shows the imported expenses noted `Imported · {Month} {Year}`.
