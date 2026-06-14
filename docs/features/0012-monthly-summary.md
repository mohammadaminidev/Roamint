# 0012 — Monthly summary (reports home)

**Status:** Done — all five steps landed. Kernel `Irr→Irt` (step 1), Wallet admits `IRT` to canonical
pairs + `/rates` IRT entry (step 2), `GetMonthlySummary` read model (step 3 — EF SQL `GROUP BY`
(category × currency), Testcontainers tests, `IExpensesQueries.GetMonthlySummaryAsync` +
`CategorySpendItem` DTO), Tabler icon font + emoji retrofit (step 4), reports home `Home.razor` at `/`
(step 5 — month switcher, USD/AMD/IRT denomination re-fold, per-category convert-then-sum, grand total,
prior-month delta, missing-rate banner → `/rates`, symmetric 5-slot nav + `/categories` placeholder).
Next feature: category management.
**Lands in (proposed):** `src/Modules/Expenses/Roamint.Expenses.Application/`,
`src/Modules/Expenses/Roamint.Expenses.Infrastructure/`,
`src/Modules/Expenses/Roamint.Expenses.Contracts/`, `src/Bootstrap/Roamint.Web/`,
`src/Shared/Roamint.SharedKernel/` (IRT rename), `src/Modules/Wallet/...` (IRT in canonical pairs, D5),
`tests/Modules/Expenses/...`, `tests/...` (kernel/Wallet test updates)

## Context

Capture and the converting list are done (0006–0011). The payoff that replaces the Excel workflow is the
**monthly summary**: for a month, **per-category totals in one display currency**, each with its
**prior-month delta**. This is the reports **home** — 0011 already redirects `/` → `/expenses` as a
placeholder *"home later becomes a reports dashboard"*; this slice makes `/` that dashboard and leaves the
full list at `/expenses`.

It is a pure **read/analytics** slice — no new aggregate, no domain invariant. It reuses the 0010/0011
machinery (latest-rate host-side conversion, missing-rate → `/rates` routing) and introduces **Dapper**,
the read path the architecture has reserved for analytics since 0005.

## Decisions

### D1 — `/` is the monthly summary; `/expenses` stays the full list

Replace the `/` → `/expenses` redirect with a `Home.razor` reports page. The list, add, and rates screens
are unchanged.

**Bottom nav goes to a symmetric 5-slot bar** (decided 2026-06-14). Adding a Reports tab to the current
3-slot bar (`Expenses · ＋ · Rates`) would leave it lopsided (2 left, 1 right of the FAB). Balance it with a
**Categories** tab — the last unchecked V1 item (create/rename/archive/merge), which currently has nowhere
to live. Final order: **Reports · Expenses · ＋ · Rates · Categories** — a clean split of *consume* (left)
vs. *set up* (right) around the add FAB. The Categories screen itself is built in the follow-on slice; this
slice only adds the nav slot (routing to a placeholder until then).

### D2 — Read model returns per-(category, currency) sums; the host converts and aggregates

Conversion is latest-rate and host-side (0010 D5) — the module **must not** convert (0002 "no FX in the
domain"). So the read model cannot return one number per category in a display currency. It returns the
**raw, lossless** breakdown and the host folds it:

```
MonthlySummary(DateOnly From, DateOnly To) -> IReadOnlyList<CategorySpendRow>
CategorySpendRow(Guid CategoryId, string CategoryName, string CurrencyCode, decimal Total)
```

One row per (line category × expense currency) with `SUM(line.amount)` over the range. Category is
**line-level** (an expense's lines can differ in category), so we sum line amounts grouped by
`line.category_id` and the expense's currency — consistent with the 0011 list's line-level category match.
The host: for the chosen display currency **D**, per category, sum `TryConvert(total, currency, D)` over its
currency buckets; categories ordered by converted total descending; a grand total in **D**.

**Prior-month delta:** the page calls the query **twice** (current month, prior month), converts both to D,
and diffs per category. A category present in only one month shows the full ±. Same latest-rate caveat as
the list (0010): last month's totals are *"what they're worth today"*, not a frozen historical number —
acceptable and consistent, and a read-only switch to historical rates stays possible later.

### D3 — Missing rate ⇒ alert + route to `/rates`, never a silent partial total

Reuse `RateConverter.TryConvert` (0011 D2). If any currency present in the visible months can't reach the
chosen display currency, the page shows the 0011-style banner naming the pair(s) and links to `/rates`
rather than summing a best-effort/partial number. `All`-style "show originals" makes no sense for a
single-number-per-category summary, so the display control is **always a single currency** here (unlike the
list's `All` default) — default **AMD** (the local cash currency the user reconciles against).

**Display currency set drops USDT** (decided 2026-06-14): as a *display target* USDT ≈ USD, so it's
redundant. The denomination control offers **USD / AMD / IRT** (plus `All` on the list). USDT stays fully
**transactable** (still an expense's original currency, still shown in originals) — this only removes it as
a view-in target. The list's "View in" control was updated to match (`USDT` → `IRT`).

### D4 — Keep EF for this read model (Dapper deferred) — **decided 2026-06-14**

The `MonthlySummary` handler lives in Infrastructure (read models bypass the aggregate) and is a single
LINQ `GroupBy` over `expense × line × category`, projected with `AsNoTracking` — the established pattern of
the other Expenses read models. At personal scale the grouped `SUM` translates cleanly; introducing Dapper
here would add a dependency for a trivial query. **Dapper stays deferred** until a read genuinely needs raw
SQL (e.g. window functions for trends). The architecture's "Dapper for analytics" note in
`ListActiveCategoriesHandler` is updated to reflect this — Dapper arrives with the *first query EF can't
express well*, not the first analytics screen.

### D5 — Toman as **IRT**, conversion ships this slice — **decided 2026-06-14**

Per the user: **do not model toman as IRR-shown-as-rial/10; use a first-class `IRT` (toman),** and ship
toman conversion **in this slice** (not a follow-on). The shared kernel currently has
`Currency.Irr = new("IRR", 0)` *"shown as toman = rial / 10"* — the exact approach being rejected.

- **Kernel:** rename `Currency.Irr` → `Currency.Irt = new("IRT", 0)` (toman is quoted in whole units; 1
  toman = 10 rial, but we store toman directly — no /10 display hack). Blast radius is tiny: `Currency.All`,
  one Wallet `CurrencyPair` guard + its test, and one Expenses `CurrencyTests` `InlineData` row. IRT remains
  **non-transactable** — it is never an expense's original currency (no cash/USDT wallet in toman), only a
  **display target**. The add-expense currency control is unchanged (still USD/AMD/USDT).
- **Toman conversion:** showing the summary in toman needs a `USD→IRT` rate. Today Wallet **excludes** the
  Iranian currency from canonical pairs (`CurrencyPair.Canonical` throws). To support toman: extend the
  canonical priority to `USD > USDT > AMD > IRT`, let `SetRate("IRT", tomanPerUsd)` store a `USD/IRT` pair
  (the `SetRateRequest` shape is already generic), and the converter triangulates `AMD→USD→IRT` for free.
  IRT only ever appears as `to` in `TryConvert`, never `from`, so there is no reverse-rate ambiguity and no
  risk of an IRT-denominated expense.

### D7 — Tabler outline icons replace emoji, app-wide — **decided 2026-06-14**

Per the user: drop the emoji icon map (`🍽️ 🚕 🏠 🛒 ⚡` in `ExpenseList.razor`, nav glyphs in `NavBar.razor`)
for **Tabler outline icons** everywhere. **Self-host the webfont in `wwwroot`** (a `<link>` in `App.razor`
next to `app.css`) — *not* a CDN — so the phone-over-hotspot flow never depends on an external host being
reachable. Category icons map by name (`ti-home`, `ti-shopping-cart`, `ti-tools-kitchen-2`, `ti-car`,
`ti-bolt`, fallback `ti-tag`); nav uses `ti-chart-pie / ti-notes / ti-plus / ti-arrows-exchange / ti-tag`.
Retrofit the two existing screens in the same slice that introduces the font. Outline variants only (no
`-filled` suffixes).

### D6 — Conventions reused verbatim

`TimeProvider` for "current month" (no `DateTime.Now`); primitive-DTO Contracts facade
(`IExpensesQueries` gains `GetMonthlySummaryAsync`); typed ids stay internal; no cross-schema read
(categories share the Expenses schema, so the category-name join is in-module); host composes Expenses
rows + Wallet rates exactly as 0011.

## Domain model

No new aggregate, value object, or event. One **shared-kernel** change: `Currency.Irr` → `Currency.Irt`
(D5). All else is read models + UI.

## Public surface (additions)

`IExpensesQueries` gains:

```csharp
/// Per-(category, currency) spend sums over a date range, for the host to convert and aggregate into a
/// display-currency monthly summary. Category is line-level; currency is the expense's original currency.
Task<IReadOnlyList<CategorySpendItem>> GetMonthlySummaryAsync(
    DateOnly from, DateOnly to, CancellationToken ct = default);

public sealed record CategorySpendItem(
    Guid CategoryId, string CategoryName, string CurrencyCode, decimal Total);
```

No write-API change. Wallet's surface is unchanged unless toman conversion ships here (D5), which adds only
the already-generic `SetRate("IRT", …)` path — no new Contracts type.

## Decided (2026-06-14)

- **Toman ships in this slice as first-class `IRT`** (D5) — kernel rename + IRT in Wallet canonical pairs +
  IRT in the denomination control and `/rates` manual entry, all here.
- **Keep EF for the read model** (D4) — Dapper deferred until a query needs raw SQL.

## Open questions

- ~~**Prior-month delta presentation**~~ **Resolved (step 5):** the delta is the **absolute converted
  change** in the display currency (no percentage) — `▲ <amount>` in warning amber when spend rose, `▼
  <amount>` in green when it fell, `no change` when flat. A category with **no prior baseline** shows a
  blue `new` pill instead of a delta; one **gone this month** sorts to the bottom with a muted `0` total and
  the full `▼` drop. Rows order by current-month converted total descending.

## Verification

1. `dotnet build` / full `dotnet test` green (warnings-as-errors); Dapper pinned in CPM.
2. **Read-model tests** (Testcontainers): `GetMonthlySummary` groups by line-category × currency, sums line
   amounts, joins category names, honours the date range; mixed-currency and mixed-category expenses split
   into the right buckets; empty range ⇒ empty.
3. **Host aggregation tests** (Web): per-category convert-then-sum into the display currency; grand total;
   prior-month delta (present-both, new-this-month, gone-this-month); missing-rate path raises the banner and
   does **not** emit a partial total. If toman ships: `AMD→USD→IRT` triangulation rounds to IRT whole units.
4. **Manual run** (Aspire): record mixed-currency expenses across two months; `/` shows per-category totals
   in AMD with prior-month deltas; switching the denomination reconverts; a missing rate routes to `/rates`
   and re-converts once entered.

## Implementation order (reviewable steps)

1. **Kernel: `Irr` → `Irt`** — rename + comment + the two test references. Pure correctness, no behaviour.
2. **Wallet: admit `IRT` to canonical pairs** — extend `CurrencyPair` priority to `USD > USDT > AMD > IRT`;
   flip the excluding test to assert `USD/IRT` canonicalizes; confirm `SetRate("IRT", tomanPerUsd)` stores a
   `USD/IRT` row end-to-end. No new Contracts type (the generic `SetRateRequest` already covers it).
3. **`GetMonthlySummary` read model (EF)** — `GroupBy` over expense × line × category, `AsNoTracking`,
   `CategorySpendView`, Infrastructure handler, `IExpensesQueries.GetMonthlySummaryAsync` + DTO.
   Testcontainers tests.
4. **Tabler icon font (D7)** — self-host the outline webfont under `wwwroot`, link it in `App.razor`,
   retrofit `ExpenseList.razor` + `NavBar.razor` off emoji. Cosmetic, no behaviour; do it before the reports
   screen so the new page is born on Tabler.
5. **Reports home (`/`)** — `Home.razor`: month picker (default current month via `TimeProvider`),
   denomination control (default AMD, incl. IRT), per-category rows convert-then-sum, grand total,
   prior-month delta, missing-rate banner → `/rates`; symmetric 5-slot nav with a Categories placeholder
   (D1); `/expenses` keeps the list. `/rates` manual entry gains IRT.

## What's next

After the summary: **category management** (create/rename/archive/merge, no destructive delete — the last
unchecked V1 item), then V2 (Receipts OCR, Insights NL queries, spreadsheet export/import). Wallet balances
and the outbox drain move to **V3+** (2026-06-14 decision).
