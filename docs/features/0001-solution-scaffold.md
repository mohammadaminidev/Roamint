# 0001 — Solution scaffold

**Status:** Done
**Lands in:** repo root, `src/Hosting/`, `src/Bootstrap/`, `docs/`

## Context

Bootstrap the repository with a buildable, runnable shell so all subsequent module work has a stable home. Goal: `dotnet run` on the Aspire AppHost brings up Postgres (`pgvector/pgvector:pg17`), PgAdmin, and a Blazor Server placeholder, with the Aspire dashboard reporting green across the board.

This step writes **no domain code**. Modules are designed before they are scaffolded.

## Decisions

- **Solution format: `Roamint.slnx`** — modern XML solution format, supported by .NET 10 CLI and VS 2022 17.10+.
- **Aspire 13.3.2**, not 9.x. Aspire's versioning realigned to track .NET releases — the train jumped numbering. The AppHost csproj uses both `Microsoft.NET.Sdk` and a nested `<Sdk Name="Aspire.AppHost.Sdk" Version="$(AspireAppHostSdkVersion)" />`. The SDK version is held in `Directory.Packages.props` as the `AspireAppHostSdkVersion` MSBuild property.
- **Central Package Management** enabled (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`, `CentralPackageTransitivePinningEnabled=true`). New packages must be added with a matching `<PackageVersion>` entry there.
- **Repo-local `NuGet.config`** scopes the project to nuget.org only. Required because CPM raises NU1507 when multiple sources are visible without explicit source mapping (the developer machine has internal company feeds configured globally).
- **`TreatWarningsAsErrors=true`** with `AnalysisLevel=latest-recommended`. This is strict on purpose: `CA1848` (use `LoggerMessage` source generator) and `CA1873` (lazy log arg evaluation) will block sloppy logging in any future code.
- **No global `RootNamespace`.** Each project keeps its assembly-name default, so namespaces like `Roamint.Web.Components` resolve correctly. Setting `RootNamespace=Roamint` repo-wide silently collides with per-project names.
- **Persistent Postgres volume + PgAdmin sidecar** in dev. Data survives AppHost restarts via the `roamint-pgdata` Docker volume. PgAdmin makes ad-hoc inspection easy without leaving the Aspire dashboard.
- **No modules in this step.** `src/Modules/`, `src/Shared/`, and `tests/` are placeholder folders with `.gitkeep` files.
- **One-off startup log removed.** An initial plan included `app.Logger.LogInformation` to confirm the `roamintdb` connection string was injected. `CA1848`/`CA1873` blocked it under `TreatWarningsAsErrors`; the Aspire dashboard's resource graph proves the wiring without it.

## Public surface

- **`AddServiceDefaults<TBuilder>(this TBuilder)`** — extension on `IHostApplicationBuilder` in `Roamint.ServiceDefaults`. Wires OTel (logs, metrics, traces), default health checks, service discovery, and Polly standard resilience for the default `HttpClient`. Activates the OTLP exporter when Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT`.
- **`MapDefaultEndpoints(this WebApplication)`** — exposes `/health` (full) and `/alive` (live-tag predicate) in Development only.
- **`Roamint.Web`** at `/` — placeholder Blazor Server page; `<h1>Roamint</h1>` and a note pointing to the Aspire dashboard.

## Folder layout

```
src/
  Hosting/
    Roamint.AppHost/         — Aspire orchestration
    Roamint.ServiceDefaults/ — OTel + health + Polly + service discovery
  Bootstrap/
    Roamint.Web/             — Blazor Server host
  Modules/      (.gitkeep)
  Shared/       (.gitkeep)
tests/          (.gitkeep)
docs/
  README.md
  features/
    0001-solution-scaffold.md
```

## Verification

1. `dotnet restore` — clean, no NU1507.
2. `dotnet build` — 0 warnings, 0 errors.
3. `dotnet run --project src/Hosting/Roamint.AppHost --launch-profile http` — dashboard URL appears in stdout.
4. In the dashboard, `postgres`, `pgadmin`, and `web` resources are all green.
5. Web endpoint serves `<h1>Roamint</h1>` at `/`.
6. Ctrl+C tears everything down; restart preserves the `roamint-pgdata` volume.

## Open questions

- **Microsoft.Extensions.AI provider for Anthropic** — first-party package, community wrapper, or a hand-rolled `IChatClient`? Defer until the Insights module begins.
- **xUnit v2 vs v3** for the first test project — defer until the first test project is added (likely with the Expenses domain).
- **AnalysisLevel** — if `latest-recommended` keeps blocking idiomatic code in module work, consider relaxing to `latest-minimum` or selectively setting `CodeAnalysisTreatWarningsAsErrors=false`.

## What's next

The **Expenses** module — domain model design (Expense, Category, Item, Budget aggregates with their invariants and events) written in `docs/features/0002-expenses-domain.md` *before* any code lands in `src/Modules/Expenses/`.
