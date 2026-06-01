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
| Expenses  | V1 in progress | Headline. Domain/Application/Infrastructure/Contracts done (0002/0004/0005). Add-expense form core + read seam done (0006); line items + item autocomplete + category suggestion done (0007). Outbox capture done; drain deferred until a consumer. Next: expense list, then Dapper read models + outbox drain (with Wallet). |
| Wallet    | Not started | Plumbing. Wallets per currency + Exchange events as rate timeline. |
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
- **Next**: expense list (filter by date/category/currency). Later: Dapper read models + outbox drain (with Wallet).

## Stop conditions

Do not generate code in `src/Modules/`, `src/Shared/`, or `tests/` until the corresponding feature doc has been written and the user has approved the design.
