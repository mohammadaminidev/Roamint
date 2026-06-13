# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project north star

Roamint is a single-tenant, multi-currency expense tracker built for personal daily use by an expat in Armenia juggling cash USD, cash AMD, and USDT. The architecture must be the *correct tool for real domain complexity* — never over-engineered, never under-engineered. The repo also serves as a portfolio piece for senior backend roles, so design discipline matters as much as shipping.

## How to work with the user

- **Design before code for any new bounded context or major feature.** Propose the domain model — aggregates, value objects, invariants, events — in writing first. Wait for explicit approval before generating code in `src/Modules/...`.
- **Small, reviewable changes.** One aggregate, one handler, one endpoint per step. Tests alongside.
- **Be skeptical, including of the user's choices.** Push back if a request contradicts a prior decision or pushes toward over-engineering.
- **No phantom dependencies.** Always verify a NuGet package exists at the version you intend to pin — `dotnet package search <name> --take 3 --format json`.
- **Don't gold-plate.** No XML doc comments on private methods, no factories-of-factories, no premature interfaces. Add an interface only when there is a real second implementation or a real test seam.
- **Match style.** The user is a senior .NET engineer with DDD experience. Skip basic-pattern explanations. Show reasoning when choosing between two reasonable approaches at the persistence, async, or AI layer.
- **Privacy.** Repo is public, data is not. Seed data and screenshots must use anonymized values. Real `.env` and DB dumps are gitignored.

## Architecture invariants

- **Modular monolith.** All modules deploy as a single Blazor Server host (`Roamint.Web`).
- **Each module owns its own DB schema** (`expenses.`, `receipts.`, `wallet.`, `insights.`). **No foreign keys cross schema boundaries.**
- **Cross-module communication is via integration events**, dispatched in-process through a **transactional outbox**. Never call across module boundaries directly.
- **Module layering** (inside each `src/Modules/<Name>/`):
  - `Domain` — aggregates, value objects, domain events; no infrastructure dependencies.
  - `Application` — command/query/event handlers, validators.
  - `Infrastructure` — EF Core mappings, Dapper queries, outbox processor, external clients.
  - `Contracts` — public DTOs + the module's `AddXxxModule(IHostApplicationBuilder)` wiring extension (hangs off the builder so it can use Aspire's `AddNpgsqlDbContext`).
- The web host only references each module's **`Contracts`** project, never its Domain/Application/Infrastructure.
- **EF Core for writes, Dapper for reads** (especially analytics).

## Domain patterns

- Aggregates have private setters and factory methods; invariants guarded inside the aggregate.
- Domain events captured during `SaveChanges`, persisted to the outbox in the same transaction, dispatched after commit.
- `TimeProvider` injected everywhere — never call `DateTime.UtcNow` directly.
- IDs via `Guid.CreateVersion7()` for time-ordered B-tree locality.
- **Strongly-typed IDs**: `ExpenseId`, `CategoryId`, etc. — `record struct` wrappers over `Guid`.

## Modules

| Module    | Status      | Notes                                                              |
|-----------|-------------|--------------------------------------------------------------------|
| Expenses  | V1 in progress | Headline. Domain/Application/Infrastructure/Contracts done (0002/0004/0005). Add-expense form core + read seam done (0006); line items + autocomplete + category suggestion (0007); line-level categories + quantities (0008); scoped/global tags + per-line picker (0009). Outbox capture done; drain deferred until a consumer. Next: expense list, then Dapper read models + outbox drain (with Wallet). |
| Wallet    | V1 in progress | **Exchange-rates slice (0010) done** — Domain/Application/Infrastructure/Contracts all shipped, wired into the host. `Exchange` (swap) + `ExchangeRate` (current rate per canonical `CurrencyPair`, USD-spoke, latest-wins, manual/exchange source); `RecordExchange`/`SetRate` + `GetCurrentRates`; EF `wallet.` schema (`pair` text key via converter), outbox capture, `IWalletApi`/`IWalletQueries`, `AddWalletModule`, `InitialCreate` migration. 38 tests. Next: `0011` converting expense list consumes `IWalletQueries`. Wallets-per-currency balances + outbox drain come after. |
| Receipts  | V2+         | OCR pipeline — out of V1.                                          |
| Insights  | V2+         | Embeddings, NL queries — out of V1.                                |

## Tech stack pins

- **.NET 10** (SDK band pinned via `global.json`, currently 10.0.200 + `latestFeature` roll-forward).
- **Aspire 13.3.2** (the public version train — do *not* assume Aspire 9.x; versioning realigned to track .NET releases).
- Microsoft.Extensions.* at **10.6.x**.
- OpenTelemetry at **1.15.x**.
- EF Core 10 + Npgsql + Pgvector when modules need persistence.
- **Hand-rolled in-process command/query/event dispatcher. No MediatR** (licensing).
- Microsoft.Extensions.AI as the LLM abstraction (OpenAI + Anthropic providers).
- xUnit + Shouldly + NSubstitute + Testcontainers.PostgreSql — introduced with the first test project.

Central Package Management is on (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`, `CentralPackageTransitivePinningEnabled=true`). Add new packages with a matching `<PackageVersion>` entry there, not inline.

## Common commands

PowerShell or pwsh (Windows dev machine). All paths use forward slashes — backslashes get eaten by shell escaping in some contexts.

```powershell
# From repo root.

# Restore + build everything.
dotnet restore
dotnet build

# Run the full stack via Aspire (Postgres + PgAdmin + Web).
dotnet run --project src/Hosting/Roamint.AppHost --launch-profile http

# Verify a package version before pinning it in Directory.Packages.props.
dotnet package search <PackageName> --take 3 --format json

# Check for outdated central versions.
dotnet list package --outdated
```

The Aspire dashboard URL appears in AppHost stdout: `Login to the dashboard at http://localhost:15043/login?t=...`. The dashboard is the source of truth for resource health, live logs, traces, and metrics.

## Folder layout

```
src/
  Hosting/
    Roamint.AppHost/         — Aspire orchestration (dev only).
    Roamint.ServiceDefaults/ — OTel + health + Polly + service discovery for any host.
  Bootstrap/
    Roamint.Web/             — Blazor Server host; references each module's Contracts.
  Modules/                   — Bounded-context folders (placeholder until Expenses lands).
  Shared/                    — Shared kernel + dispatcher (placeholder).
tests/                       — Test projects (placeholder).
docs/
  features/                  — One markdown per feature/step. See docs/README.md.
```

## Documentation convention

**Every feature gets a markdown document** under `docs/features/<NNNN>-<slug>.md` (4-digit sequential prefix). Required sections:

- **Context** — why this work is happening, what it solves.
- **Decisions** — non-obvious choices and their rationale.
- **Domain model** (for module work) — aggregates, value objects, invariants, events.
- **Public surface** — DTOs/endpoints/types exposed to other modules or the web host.
- **Open questions** — items still to resolve.
- **Verification** — how the feature is exercised end-to-end.

Write the doc **before** the code. When proposing work, reference the doc filename in the plan.

The README's V1 checklist is the authoritative status board; update it when a feature lands.

## Notable decisions / non-obvious traps

- **`Directory.Build.props` must not set `RootNamespace`.** It overrides per-project assembly names (e.g., `Roamint.Web` becomes `Roamint`, breaking `using Roamint.Web.Components;`). Let each project keep its default.
- **`NuGet.config` at repo root scopes to nuget.org only.** CPM raises NU1507 if multiple feeds are visible without explicit source mapping. The dev machine has internal company feeds configured globally; they are deliberately excluded here.
- **`TreatWarningsAsErrors=true` + `AnalysisLevel=latest-recommended` blocks `LogInformation` calls on hot paths** via `CA1848` (use `LoggerMessage` source generator) and `CA1873` (lazy arg evaluation). Use the source generator for persistent logs; strip one-off informational logs.
- **Aspire AppHost csproj uses both `Microsoft.NET.Sdk` and a nested `<Sdk Name="Aspire.AppHost.Sdk" Version="$(AspireAppHostSdkVersion)" />`.** The SDK version is held in `Directory.Packages.props` as the `AspireAppHostSdkVersion` MSBuild property — single source of truth.
- **Forward slashes for paths in Bash on Windows.** `dotnet run --project src\Hosting\...` gets its backslashes consumed by shell escaping. Use `src/Hosting/...`.
- **Aspire resource startup is async**, and per-resource logs stream to the dashboard, not AppHost stdout. To verify health programmatically, use `docker ps`, the resource service endpoint, or just click into the dashboard.
- **EF Core 10 + Npgsql 10 persistence traps** (hit in 0005; will recur for Wallet):
  - `ComplexProperty` works on `EntityTypeBuilder` but **not on owned-collection builders** (`OwnsMany`). Map value objects on owned entities with a `ValueConverter` instead (see `MoneyConverter`); keep the two-column complex mapping for aggregate-root value objects you need to query/group by.
  - Npgsql 10 **removed `UseXminAsConcurrencyToken`**; the manual `Property<uint>("xmin")` form scaffolds an invalid real `xmin` column (Postgres reserves it). Optimistic concurrency is currently deferred.
  - **Pin `Microsoft.EntityFrameworkCore` + `.Relational`** in `Directory.Packages.props`, or the Npgsql provider and Design tooling drag in mismatched 10.0.x assemblies → MSB3277.
  - EF-scaffolded **migrations need a folder-scoped `.editorconfig` with `generated_code = true`**, else `IDE0161`/style rules fail the build under warnings-as-errors. `dotnet-ef` is a local tool (`.config/dotnet-tools.json`) — run via `dotnet dotnet-ef ...`.
  - Each module's `AddXxxModule` registers its own handlers via `AddApplicationDispatcher`; the three dispatchers register idempotently (`TryAdd`), so the host stays ignorant of Application assemblies. `AddApplicationDispatcher` takes `params Assembly[]` — Expenses scans **both** Application (command/event handlers) and Infrastructure (read-model **query** handlers like `ListActiveCategoriesHandler`), since read models that bypass the aggregate belong in Infrastructure. Anchor the Infrastructure scan on the public `ExpensesPersistence` type (the `DbContext` is internal).
  - **Multi-module persistence (hit in 0010, Wallet):** modules share one Postgres database (`roamintdb`) but each owns a schema. **`HasDefaultSchema("<module>")` also relocates that context's `__EFMigrationsHistory` into its schema**, so two `DbContext`s migrating the same database don't collide — no explicit `MigrationsHistoryTable` needed. **Complex-type *constructor* binding is not used anywhere** — give each aggregate a single `private T(TId id) : base(id)` constructor and set state via private setters (factory/methods); EF materializes complex `Money` and scalars through backing fields. A fully-bindable multi-arg domain ctor risks EF picking it and attempting unsupported complex-ctor binding. **A natural/composite aggregate key** (Wallet's `CurrencyPair`) maps cleanly as a **single text column via a `ValueConverter`** (`"BASE/QUOTE"`), reconstructed through the canonical factory; don't project the converted key in SQL — materialize then read its parts in memory.
- **Blazor scoped CSS needs an explicit bundle link.** `App.razor` must reference `@Assets["Roamint.Web.styles.css"]` (the scoped-CSS bundle) in addition to `app.css`, or `*.razor.css` styles silently don't load. Global theme (`:root` vars, `body`) lives in `app.css`; per-component layout lives in scoped `*.razor.css`.

## Current state

Authoritative status lives in `docs/features/` (one doc per slice) and the README V1 checklist — trust those over this section, and keep this section in sync when a slice lands.

- `0001` Solution scaffold — done.
- `0002` Expenses **Domain** — done (`src/Modules/Expenses/Roamint.Expenses.Domain`).
- `0003` Dispatcher — done (`src/Shared`).
- `0004` Expenses **Application** — done (`src/Modules/Expenses/Roamint.Expenses.Application`).
- `0005` Expenses **Infrastructure + Contracts** — done. EF Core `expenses.` schema, repositories/UoW, **outbox capture** (write-only; drain deferred until a consumer), `AddExpensesModule` wiring on `IHostApplicationBuilder`, dev-only startup migration. `IExpensesApi` is the host's only entry point. Strongly-typed IDs stay module-internal behind primitive DTOs. `xmin` optimistic concurrency deferred (Npgsql 10 dropped the helper). EF migrations use the `dotnet-ef` local tool (`.config/dotnet-tools.json`).
- `0006` Expenses **add-expense form (core) + read seam** — done. `IExpensesQueries` (read API) alongside `IExpensesApi`; `ListActiveCategories` query handled by a **read model in Infrastructure** (`AsNoTracking`), dispatched via `AddApplicationDispatcher` now scanning both the Application **and** Infrastructure assemblies. Dev migration service seeds a starter category set on an empty DB. `/add` Blazor form (mobile-first, ported from `docs/prototypes/0006-add-expense-mock.html`): amount + currency (USD/AMD/USDT) + category chips + date + note → `RecordExpenseAsync`; `DomainException` surfaced as inline validation. Currency-aware decimal handling lives in the host as a UI concern (no SharedKernel reach beyond the documented `DomainException`).
- `0007` Expenses **line items + item autocomplete + category suggestion** — done. No domain changes (lines already shipped in 0002/0004/0005); this slice is read-side + UI. Two new EF `AsNoTracking` read models in Infrastructure — `SuggestItemsHandler` (corpus = distinct notes ∪ line descriptions, `ILIKE` filter in SQL, prefix-before-contains + recency ranking + carried category in memory) and `SuggestCategoryHandler` (most-recently-used category among matches, frequency breaks same-date ties) — surfaced on `IExpensesQueries` as `SuggestItemsAsync` (returns `ItemSuggestion(Text, CategoryId)` so a pick fills text **and** lights the chip in one round trip) and `SuggestCategoryAsync` (free-text-blur path). `/add` form gains expandable line rows (description+qty+price); **when ≥1 line exists the amount is derived and read-only**, computed with the same per-line `Money` rounding the aggregate uses, so the UI structurally can't trip the line-sum invariant. Autocomplete is debounced (~250 ms, ≥2 chars) for the hotspot flow; category suggestion never overrides a manual chip tap. **EF trap:** filtering an owned collection via `SelectMany(...).Where(ILike)` doesn't translate — push the `ILike` predicate **inside** the navigation (`expense.Lines.Where(...).Select(...)`); `expense.Lines.Any(ILike)` in a top-level `Where` translates fine.
- `0008` Expenses **line-level categories + quantities** — done. Aggregate reshape: category + paid `Amount` move **onto the line**; `Expense` is one payment (one currency/date, ≥1 line, derived `Total`). `Quantity` is a typed optional value object over a closed canonical unit set (`g`/`ml`/`Pcs`), never a multiplier. `ExpenseLine` became a **regular child entity** (not owned) so its `Money` uses the two-column `ComplexProperty` mapping (`MoneyConverter` deleted); schema re-scaffolded. `ExpenseRecorded`/`ExpenseCorrected` carry only `(Id, Total, OccurredOn)`; `ExpenseRecategorized` removed. `/add` is line-oriented; total is read-only/derived.
- `0009` Expenses **tags (scoped + global)** — done. New `Tag` aggregate parallel to `Category` (name, nullable `Scope` = category-id or global, Active/Archived), events `TagCreated/Renamed/Archived`. `ExpenseLine.TagIds` (0..n) persists as an `OwnsMany` `LineTag` collection → `expenses.expense_line_tags` (PK `(expense_line_id, tag_id)`; cascade with the line; **no DB FK to `tags`** and **no `scope_category_id` FK** — the same app-enforced id-reference convention `0008` uses for `category_id`). Scope/active compatibility is validated in the Record/Correct **handler** (`EnsureCompatibleTagsAsync`, mirrors `EnsureActiveCategoriesAsync`); the "archived classifier blocks correction" friction is an accepted shared limitation, not fixed here. `CreateTag` is idempotent on the active `(name, scope)` pair (case-insensitive) — backs inline create. `IExpensesQueries.ListTagsAsync(categoryFilter)` returns global ∪ scoped for the picker; `ItemSuggestion` gained `TagIds` (suggestion remembers a line's tags). `/add` gains a per-line tag picker (filtered by the line's category, drops incompatible scoped tags on category change) + inline create (default scoped-to-line-category, global toggle). Seed adds `Fruit`/`Vegetables`/`Dairy` (scoped to Groceries) + `For Happiness`/`Gift`/`Essential` (global).
- `0010` **Wallet — exchange-rates slice** (bootstraps the Wallet module to enable expense-list display conversion). User decisions: convert at the **latest known rate** (not historical — accepted total-drift), rates fed by **exchange transactions with a manual override**, **triangulate through USD** (manual entry is USD-anchored, "Quote per 1 USD"). **Domain done** (`src/Modules/Wallet/Roamint.Wallet.Domain`): `Exchange` aggregate (swap; `ImpliedRate`, `ExchangeRecorded`), `ExchangeRate` aggregate keyed by canonical `CurrencyPair` (USD>USDT>AMD priority) — `FromObservation`/`Observe` normalize any direction to canonical (reciprocal), latest-wins by `ObservedAt`, `ExchangeRateChanged` only on value change; `RateSource` (Exchange|Manual). 18 domain tests green. Remaining 0010 steps: Application (record-exchange/set-rate handlers + get-rates query) → Infra+Contracts (`wallet.` schema, outbox capture, `IWalletApi`/`IWalletQueries`, `AddWalletModule`). Conversion is composed **host-side** (no cross-schema read); "All" filter = original currencies; a missing rate **alerts + routes to a manual exchange-rate page**, never silently falls back. **Infrastructure + Contracts done** (`Roamint.Wallet.Infrastructure`/`.Contracts`): EF `wallet.` schema (`exchanges`/`exchange_rates`/`outbox_messages`), `ExchangeRate`'s canonical `CurrencyPair` key persists as a single `pair` text column via `CurrencyPairConverter` (reversed lookups canonicalize to the same row); `Exchange.From/To` are two-column `ComplexProperty` money; outbox capture (write-only); `GetCurrentRates` read model (`AsNoTracking`, projects `Id.Base/Quote` in memory); `IWalletApi`/`IWalletQueries` primitive facade; `AddWalletModule` wired into the host; `InitialCreate` migration. Aggregates use a single id-binding ctor + private setters (no complex-ctor binding). Two DbContexts share `roamintdb`; `HasDefaultSchema` gives each its own `__EFMigrationsHistory` (no collision). 38 Wallet tests (18 domain / 9 app / 7 infra-Testcontainers / 4 contracts-boot).
- **Next**: `0011` — the converting expense list ("view in X" denomination + "All" = originals + exchange-rate page + missing-rate alert), composing `IExpensesQueries` rows with `IWalletQueries` rates host-side. Later: Wallet balances + outbox drain. Later: Wallet balances + outbox drain; tag-based report screens land with the monthly summary.

## Stop conditions

Do not generate code in `src/Modules/`, `src/Shared/`, or `tests/` until the corresponding feature doc has been written and the user has approved the design.
