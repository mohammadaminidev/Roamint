# 0022 — Duplicate-expense guard

## Context

A double-submit defect (UI resubmitting one save action in a tight loop) produced 25 duplicate
expenses across May 2026 — a 21× sushi meal on the 28th, plus bread/grocery twins on the 14th and
22nd. They were cleaned up by hand against the database. The cleanup is reactive; this feature adds a
*proactive* prompt so an accidental twin is caught at entry time instead of weeks later in a manual
audit.

The signal observed in every cluster was the same: **two expenses on the same day with the same
total, in the same currency**, created seconds-to-minutes apart. That is the exact condition we warn
on.

## Decisions

- **D1 — This is a UX guard, not a domain invariant.** The domain legitimately permits two expenses
  with the same total on the same day (you really might buy 130 AMD of bread twice). Nothing was
  violated when the duplicates were created — they are valid aggregates. So the guard must **not**
  live in the `Expense` aggregate or surface as a `DomainException`, and the write path
  (`RecordExpense` command/handler) is **unchanged**. It is a read-side check plus a UI confirmation:
  a soft "did you mean to?", never a block.

- **D2 — Match key: same `OccurredOn` + same currency + same total.** Total is the line-derived,
  per-currency-rounded sum — the same arithmetic the aggregate computes and the form's `DerivedTotal`
  shows, so the two are exactly comparable. We deliberately do **not** also require an identical
  line-set: the May-14 bread twin had a corrected line quantity (`588 g` vs a bogus `0.5580 g`) yet
  the same 490 total, and a line-signature match would have missed it. Currency is part of the key so
  a 5,000 AMD expense never warns against a 5,000 USD one.

- **D3 — Applies to all saves (manual + capture review).** The receipt/import flows already link a
  source to an expense to prevent re-linking, but a re-reviewed capture can still mint a twin. The
  check is one cheap count query, so it runs on every save regardless of origin.

- **D4 — Read model is EF, not Dapper.** `Currency` and `Total` are `Ignore`d on the `Expense`
  mapping (derived from lines), so the count aggregates over `expense_lines` exactly like
  `GetMonthlySummary` — a per-expense `SUM` that EF translates cleanly at personal scale. Consistent
  with the other read models in `ReadModels.cs`; Dapper still waits for the first read EF can't
  express.

- **D5 — Confirm state resets on any change to the match key.** Editing an amount, adding/removing a
  line, or changing the date/currency clears the "confirmed" flag and dismisses the banner, so a
  still-colliding total re-prompts rather than silently saving on a stale confirmation.

## Domain model

No change. No new aggregates, value objects, invariants, or events.

## Public surface

New read query on the Expenses module's query API:

```csharp
// IExpensesQueries
Task<int> CountSameDayMatchesAsync(
    DateOnly date, string currencyCode, decimal total, CancellationToken ct = default);
```

- Internal slice: `Application/Queries/CountSameDayMatches.cs` —
  `record CountSameDayMatches(DateOnly Date, string CurrencyCode, decimal Total) : IQuery<int>`.
- Handler: `CountSameDayMatchesHandler` in Infrastructure `ReadModels.cs` (auto-discovered by the
  Infrastructure assembly scan; no manual registration).
- Returns the count of *existing* expenses matching the key, so the UI can name how many.

## UI

`AddExpense.Submit` gains a pre-save gate: after validation, if not already confirmed, it calls
`CountSameDayMatchesAsync(date, currency, DerivedTotal())`. A non-zero count renders an inline
confirmation banner in the card (same inline style as `error`/`success`, no modal — phone-first):

> You already logged 5,195 AMD on this date (1 existing). Save anyway?

**Save anyway** sets the confirmed flag and re-runs the save; **Review** dismisses and returns to
editing. Per D5, mutating the total/day/currency resets the flag.

## Open questions

- None blocking. A future enhancement could surface the colliding expense(s) inline (date + total +
  first item) so the user can eyeball the match rather than trust the count — deferred until the bare
  count proves insufficient.

## Verification

- Infrastructure test `CountSameDayMatchesHandlerTests`: seeds same-day expenses and asserts the
  count matches only on (date × currency × total), excluding different totals, different currencies,
  and other days.
- Manual: enter an expense identical in day/currency/total to an existing one → the banner appears;
  **Save anyway** records it; editing the amount dismisses the banner.
