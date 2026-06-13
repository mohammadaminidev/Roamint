# 0010 — Wallet module: exchange rates (display-conversion enabler)

**Status:** Proposed (design — awaiting approval before code)
**Lands in (proposed):** `src/Modules/Wallet/Roamint.Wallet.Domain/`,
`src/Modules/Wallet/Roamint.Wallet.Application/`,
`src/Modules/Wallet/Roamint.Wallet.Infrastructure/`,
`src/Modules/Wallet/Roamint.Wallet.Contracts/`,
`tests/Modules/Wallet/...`, `src/Bootstrap/Roamint.Web/`,
`Directory.Packages.props`, `Roamint.slnx`

## Context

The expense list (next UI slice) needs to render every expense in **one display currency of the
user's choosing**, converting rows stored in other currencies. The expense aggregate already stores
each payment in its **original currency and never converts** (0002 decision: "No FX in the domain") —
that stays true. Conversion is purely a **read/display** concern, and the rate it needs is the
Wallet module's job per the architecture ("Wallets per currency + Exchange events as rate timeline").

Wallet does not exist yet, so this slice bootstraps it — **scoped to rates only**. Per-currency wallet
**balances** (applying exchanges and expenses to running balances) are a deliberately separate, later
Wallet slice; this one delivers just enough to power conversion.

**User decisions driving the design** (asked 2026-06-13):

- **Conversion uses the *latest known* rate**, not a historical rate dated to the expense. One current
  rate per pair; every row converts at it. Accepted consequence: **past totals drift** as rates move —
  a month's spending, viewed in AMD, is not a fixed number. Because expenses are still stored in their
  original currency, switching to historical rates later is a **read-side-only** change, no migration.
- **Rates are fed by the user's exchange transactions, with a manual override.** Recording a swap
  (`$100 → ֏40,000`) stamps the implied rate; the user can also set a rate by hand. **Latest wins**
  between the two by observation time.

## Decisions

### D1 — Two aggregates: `Exchange` (the swap) + `ExchangeRate` (current rate per pair)

- **`Exchange`** is the user-recorded fact: "I gave *From* money and received *To* money on *OccurredOn*."
  It is the headline Wallet concept and the input the user described. We persist it now (not rate-only):
  it is the literal source the rates derive from, it is re-derivable into events for the future
  balances/Insights consumers, and recording-without-storing would throw away the ledger.
- **`ExchangeRate`** holds the **current** rate for a directed currency pair plus its provenance. It is a
  real aggregate, not a pure projection, because the manual override is a first-class user action with its
  own invariant (`rate > 0`, base ≠ quote) and a **latest-wins merge** rule — behavior that wants a
  guarded `Observe(...)` method, not a rebuild-from-log.
- **Why not fold the rate into a read model over `Exchange` ∪ manual rows?** Manual overrides would then
  need their own store anyway, and "latest of two sources" merge logic has to live *somewhere* testable.
  One small aggregate that both sources call is simpler than a projection plus a manual-quote table.

### D2 — How an exchange updates the rate (the one-transaction question)

The `RecordExchange` command handler does two writes in one unit of work: persist the `Exchange`, then
call `ExchangeRate.Observe(impliedRate, now, Source.Exchange)` for the swap's direction. This touches two
aggregates per transaction — normally discouraged — but it is justified here: both are **Wallet-internal**,
the rate is a small derived value with no independent consistency risk, and the alternative (raise
`ExchangeRecorded`, drain it, update the rate asynchronously) needs the outbox **drain**, which is still
deferred (0005) and would be heavy plumbing for a single-user derived value. Revisit if Wallet ever grows
a third writer of rates.

### D3 — Rate semantics, base currency, and triangulation

- A rate is quoted for a **directed pair** `Base→Quote`: "1 `Base` = `Rate` `Quote`." A swap of
  `From=$100 USD` → `To=֏40,000 AMD` implies `USD→AMD = To/From = 400`.
- **Storage:** one row per **canonical unordered pair** (fixed priority `USD > USDT > AMD`), holding the
  `Base→Quote` rate; the reverse is the reciprocal. Three supported currencies ⇒ at most three rows
  (`USD/AMD`, `USD/USDT`, `AMD/USDT`).
- **Conversion `X → D`** (read-side algorithm, see Public surface): `X == D` ⇒ 1; a stored pair `{X,D}`
  ⇒ rate or its reciprocal; otherwise **triangulate through USD** (`X→USD × USD→D`). A *direct* observed
  `{X,D}` rate is preferred over triangulation when present and current — it reflects the booth rate the
  user actually got.
- **USD is the spoke (confirmed).** Matches the prototype showing USD equivalents and USDT≈USD. Manual
  rate entry is therefore always USD-anchored ("how many `Quote` per 1 USD" — see D5a/Public surface);
  exchange-sourced rates may still arrive in any direction and are normalized to canonical storage.
  `IRR` stays out of this slice — a display-only currency (shown as toman) with no cash wallet, deferred
  with the rest of multi-display work.

### D4 — Latest-wins ordering uses an observation instant, not the business date

`Exchange.OccurredOn` is a `DateOnly` (the day of the swap, for the ledger and any future historical-rate
mode). Latest-wins for the **current** rate cannot order by day alone (a manual fix on the same day as a
swap must win). So `ExchangeRate` carries an `ObservedAt` **instant** (`TimeProvider.GetUtcNow()`);
`Observe` updates only when the incoming instant is `>=` the stored one. Manual sets, being newest, win.

### D5 — Conversion happens in the host, not across a schema boundary

The expense list's conversion is composed in the **Blazor host**, which already references both modules'
`Contracts`. It pulls raw rows from `IExpensesQueries` and the current rates from `IWalletQueries`, builds
a converter, and converts for display. No cross-schema read, **no integration event needed just to render
a screen** — mirrors the 0006 precedent ("currency-aware decimal handling lives in the host as a UI
concern"). The outbox drain / Wallet subscribing to `ExpenseRecorded` belongs to the **balances** slice,
not here, so `0010` stays purely additive to the running system.

### D5a — Display modes: "All" shows original currency; a selected currency converts

The expense list's currency control has two behaviors (decided 2026-06-13):

- **"All" ⇒ no conversion.** Each row renders in its **own original currency** (the mixed view in the
  mockup). The day subtotals stay per-currency. This is the honest, lossless default.
- **A specific currency (USD / AMD / USDT) ⇒ convert every row into it** via the host converter (D3/D5).

**Missing rate ⇒ alert and route to the rate page, do not silently fall back.** When the user picks a
display currency and *any* visible row's currency can't reach it (no direct rate and no USD-triangulation
path — i.e. the user hasn't recorded an exchange or set that rate yet), the list **does not** quietly show
originals or a placeholder. It surfaces an alert naming the missing pair(s) and links straight to the
**exchange-rate page** to enter the rate manually (`IWalletApi.SetRate`); once entered, the conversion
re-runs. This makes the converter's contract explicit: it must report *which* currencies it could not
convert, not just produce best-effort numbers. (Resolves the former no-rate open question.)

This adds a small **exchange-rate management page** to the host surface (list current rates + manual
entry/override, backed by `IWalletQueries.GetCurrentRatesAsync` + `IWalletApi.SetRateAsync`). It is the
manual-override UI from D1 and the redirect target above; it ships with the `0011` list slice.

### D6 — Outbox capture: mirror Expenses (capture now, drain deferred)

Wallet gets the same write-only outbox capture as Expenses (0005): `ExchangeRecorded` is serialized into
`wallet.outbox_messages` in the same `SaveChanges` transaction; nothing drains it yet. Rationale is 0005's:
atomic capture is the part that cannot be retrofitted, and the balances/Insights consumers that will drain
it are imminent. Cheap, established pattern, no speculative public event contracts.

### D7 — Conventions reused verbatim

`TimeProvider` injected (no `DateTime.UtcNow`); IDs via `Guid.CreateVersion7()`; strongly-typed
`ExchangeId` (`record struct`); `Money`/`Currency` from the shared kernel; EF Core for writes, `wallet.`
schema, no FK across schemas; primitive-DTO `Contracts` facade (`IWalletApi` + `IWalletQueries`), typed
IDs stay module-internal; `AddWalletModule(IHostApplicationBuilder)` is the host's only entry point;
`AddApplicationDispatcher` scans Wallet's Application (+ Infrastructure for read-model query handlers).

## Domain model

### Shared kernel — no change expected

`Money`, `Currency`, `AggregateRoot<TId>` already cover the needs. A `CurrencyPair` value object (below)
is **Wallet-internal**, not kernel — only Wallet reasons about pairs.

### Wallet.Domain

**`CurrencyPair`** — `readonly record struct (Currency Base, Currency Quote)`, the `ExchangeRate`
aggregate's id. A static `Canonical(a, b)` orders the two by the fixed `USD > USDT > AMD` priority so
`{AMD,USD}` and `{USD,AMD}` map to one stored pair; guards `a != b`.

**`Exchange : AggregateRoot<ExchangeId>`**
- State: `Money From`, `Money To`, `DateOnly OccurredOn`.
- Invariants: `From.Currency != To.Currency`; both amounts strictly positive; `OccurredOn` not in the
  future (vs. injected clock).
- `ImpliedRate` ⇒ `To.Amount / From.Amount` for direction `From.Currency → To.Currency`.
- `Record(id, from, to, occurredOn, clock)` — enforces invariants; raises `ExchangeRecorded`.

**`ExchangeRate : AggregateRoot<CurrencyPair>`**
- State: `decimal Rate` (`Base→Quote`), `DateTimeOffset ObservedAt`, `RateSource Source`
  (`Exchange | Manual`).
- `Observe(rate, observedAt, source)` — guards `rate > 0`; **updates only if `observedAt >= ObservedAt`**
  (latest-wins, ties favor the newer call); raises `ExchangeRateChanged` when the value actually changes.
- The aggregate always stores the **canonical** direction; callers pass a rate for an arbitrary direction
  and the handler normalizes (reciprocal) before `Observe` — keeps the invariant "one row per pair".

**Domain events:** `ExchangeRecorded(ExchangeId, FromMoney, ToMoney, OccurredOn)`,
`ExchangeRateChanged(Base, Quote, Rate, ObservedAt, Source)` — records implementing `IDomainEvent`.
(Which become *integration* events is the balances slice's call; `ExchangeRecorded` is the obvious one.)

### Application

- `RecordExchangeCommand` → handler persists `Exchange`, then `Observe`s the implied rate (D2).
- `SetRateCommand` (manual override) → handler `Observe`s a manual rate at `now`.
- `GetCurrentRatesQuery` → **read model in Infrastructure** (`AsNoTracking`) returning every current rate
  row (the host triangulates). Lives in Infrastructure per the established "read models that bypass the
  aggregate belong in Infrastructure" rule.

## Public surface

`Roamint.Wallet.Contracts` (the host's entire view of the module):

```csharp
public interface IWalletApi
{
    // Records a swap and stamps the implied current rate for its direction.
    Task<Guid> RecordExchangeAsync(RecordExchangeRequest request, CancellationToken ct);

    // Manual override — "how many <Quote> per 1 USD". Wins over older observations.
    Task SetRateAsync(SetRateRequest request, CancellationToken ct);
}

public interface IWalletQueries
{
    Task<IReadOnlyList<CurrentRateDto>> GetCurrentRatesAsync(CancellationToken ct);
}

public sealed record RecordExchangeRequest(
    decimal FromAmount, string FromCurrencyCode,
    decimal ToAmount,   string ToCurrencyCode,
    DateOnly OccurredOn);

// Manual entry is always USD-anchored — "how many <Quote> per 1 USD", e.g. ("AMD", 400m).
public sealed record SetRateRequest(string QuoteCurrencyCode, decimal QuotePerUsd);

// One row per canonical pair; host derives any X→D (direct, reciprocal, or USD-triangulated).
public sealed record CurrentRateDto(
    string BaseCurrencyCode, string QuoteCurrencyCode,
    decimal Rate, DateTimeOffset ObservedAt, string Source);

public static IHostApplicationBuilder AddWalletModule(this IHostApplicationBuilder builder);
```

The host-side converter (a small UI helper in `Roamint.Web`, **not** a module type) takes the
`CurrentRateDto` set + a target `Currency` and exposes `Convert(Money source) -> Money` via the D3
algorithm. Returns the original `Money` unchanged when source == target. When a pair is neither direct nor
USD-triangulable, it does **not** guess — it reports the **missing source currency** to the caller (e.g. a
`TryConvert` returning a result that distinguishes "converted" from "no rate for AMD"), so the list can
raise the D5a alert and route to the rate page. "All" mode skips the converter entirely (originals).

## Decided (2026-06-13)

- **Triangulate through USD** (D3) — AMD↔USDT is computed via USD when not directly exchanged.
- **Manual rate entry is USD-anchored** (D3/D5a) — the page always asks "how many `Quote` per 1 USD".
- **Wallet outbox capture ships now** (D6) — mirrors Expenses; drain stays deferred.

## Open questions

- **Rounding of converted display values** (minor; settle with the `0011` list) — convert then round to
  the *target* currency's `MinorUnits` (`Money.Of`). The stored expense is untouched. Per-row converted
  values won't sum exactly to a converted grand total (rounding); show each currency's subtotal converted,
  not a sum of rounded rows.

## Verification

1. `dotnet restore` / `dotnet build` — clean, warnings-as-errors on.
2. **Domain tests** (`Roamint.Wallet.Domain.Tests`): `Exchange` invariants (same-currency / non-positive /
   future-date rejected; `ImpliedRate` correct); `ExchangeRate.Observe` latest-wins (older instant
   ignored, newer applied, manual beats same-instant exchange), `rate > 0` guard, canonical direction
   normalization + reciprocal.
3. **Infra tests** (Testcontainers Postgres): `RecordExchange` round-trips the `Exchange` **and** updates
   the canonical `ExchangeRate` in one transaction (rollback ⇒ neither persists); `SetRate` overrides a
   prior exchange-sourced rate; `GetCurrentRates` returns canonical rows; `ExchangeRecorded` lands in
   `wallet.outbox_messages` in the same transaction.
4. **Host converter tests**: direct, reciprocal, USD-triangulated, identity, and missing-rate paths;
   converted values rounded to the target currency's minor units.
5. `AddWalletModule` boots against Testcontainers via the dev migration hosted service; `IWalletApi` /
   `IWalletQueries` resolve and drive end-to-end.
6. Aspire boot: `wallet` schema + tables exist; record an exchange, read current rates back.

## Implementation order (proposed sub-steps, each its own reviewable PR)

1. **Wallet Domain** — ✅ done. `Exchange`, `ExchangeRate`, `CurrencyPair`, `RateSource`, events +
   `Roamint.Wallet.Domain.Tests` (18 tests green). Normalization (canonical direction + reciprocal) and
   latest-wins live in the `ExchangeRate` aggregate (`FromObservation`/`Observe`), not the future handler,
   so they're covered by domain tests.
2. **Wallet Application** — ✅ done. `RecordExchange` (returns `ExchangeId`) + `SetRate` (USD-anchored,
   "Quote per 1 USD") command handlers, the shared `ObserveRateAsync` (load-or-create the pair's rate;
   latest-wins stays in the aggregate), repository **ports** (`IExchangeRepository`,
   `IExchangeRateRepository`, `IUnitOfWork`), and the `GetCurrentRates` query + `RateView` (handler lands
   in Infrastructure). `Roamint.Wallet.Application.Tests` — 9 tests green against in-memory fakes.
3. **Wallet Infrastructure + Contracts** — ✅ done. EF `wallet.` schema (`exchanges`, `exchange_rates`,
   `outbox_messages`), repositories/UoW, outbox capture (write-only), `GetCurrentRates` read model,
   `IWalletApi`/`IWalletQueries`, `AddWalletModule`, dev migration, `InitialCreate` migration. Wired into
   the web host. `Roamint.Wallet.Infrastructure.Tests` (7, Testcontainers) + `Roamint.Wallet.Contracts.Tests`
   (4, boots `AddWalletModule` end-to-end). **38 Wallet tests total.**
4. **Expense list with display conversion** — the `0011` UI slice: host converter, the "view in X"
   screen (currency control = display *denomination*; "All" = originals), the **exchange-rate page**
   (manual entry/override), and the missing-rate alert that routes between them.

## Outcome (Infrastructure EF decisions — D-EF)

- **`CurrencyPair` aggregate key persists as a single `pair` text column** (`USD/AMD`) via
  `CurrencyPairConverter`, reconstructed through `CurrencyPair.Canonical`. Reversed-direction lookups
  (`GetAsync(Canonical(AMD, USD))`) resolve to the same row because the converter canonicalizes. The
  `GetCurrentRates` read model materializes the (tiny) table with `AsNoTracking` then reads `Id.Base/Quote`
  **in memory** — the converted key can't be projected in SQL.
- **Single id-binding constructor + private setters on both aggregates.** Complex-type *constructor*
  binding is exercised nowhere in this codebase, so `Exchange`/`ExchangeRate` expose one
  `private T(TId id) : base(id)` constructor and the factory/`Observe` set state via private setters; EF
  materializes the complex `Money` columns and the scalar rate fields through the backing fields. This
  removes any ctor-selection ambiguity (the old fully-bindable domain ctor could have tempted EF into
  complex-ctor binding).
- **Two `DbContext`s share one database** (`roamintdb`); each owns a schema and, because `HasDefaultSchema`
  also relocates the migrations-history table, its own `<schema>.__EFMigrationsHistory`. No collision, no
  explicit `MigrationsHistoryTable` config needed. Both dev migration services migrate independently.
- **`Exchange.From`/`To` use the two-column `ComplexProperty` mapping** (Exchange is a regular entity, so
  the 0005 owned-collection limitation doesn't apply); `Rate` is `numeric(18,8)` (room for reciprocal
  quotes), `Source` stored as text like `CategoryStatus`.
- **Build flake (environment, not code):** `dotnet restore` intermittently fails `NU1900` when the NuGet
  vulnerability feed times out (promoted by warnings-as-errors). Build/test with `-p:NuGetAudit=false` to
  bypass the flaky feed; nothing in the project changed.

## What's next

After this slice, the **expense list (`0011`)** consumes the converter for display-denomination totals.
Later: **Wallet balances** (apply exchanges + `ExpenseRecorded` to per-currency wallets — the slice that
finally builds the **outbox drain**), and **historical-rate mode** if latest-rate drift proves annoying.
