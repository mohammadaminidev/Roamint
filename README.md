# Roamint

Personal multi-currency expense tracker for daily use by an expat in Armenia. Multi-currency by necessity (cash USD, cash AMD, USDT), single-tenant by design.

Also a portfolio piece — modular monolith on .NET 10 with .NET Aspire, Blazor Server, PostgreSQL + pgvector, OpenTelemetry, and DDD-style bounded contexts.

> **Privacy:** This repo is public. The real data is not. Seed values and screenshots use anonymized data. The schema is single-tenant and not hardened for multi-user.

## Status

**Solution scaffold — done.** Aspire AppHost orchestrating Postgres (`pgvector/pgvector:pg17`), PgAdmin, and a Blazor Server placeholder. Build is clean, dashboard goes green.

See [`docs/features/0001-solution-scaffold.md`](docs/features/0001-solution-scaffold.md) for the design record.

### V1 roadmap — replace the Excel workflow

- [x] Solution scaffold
- [x] Expenses module — domain (Expense, Category) with strongly-typed IDs, EF Core persistence, outbox-backed events (capture done; drain lands with the first consumer)
- [ ] Add-expense form — mobile-first, one-handed thumb input, item autocomplete, category suggestion
- [ ] Expense list — filter by date range / category / currency
- [ ] Monthly summary — per-category totals in AMD / USD / IRR (toman) with prior-month delta
- [ ] Category management — create, rename, archive, merge (no destructive delete)
- [ ] Exchange-event entry — rate timeline driving per-expense FX equivalents
- [ ] Wallet view — de-emphasized; computed balances from movements

### V2+ — not yet scoped

- Receipt photo OCR (Receipts module)
- Natural-language queries over expenses (Insights module)
- Anomaly / budget alerts
- Spreadsheet export
- FIFO cost basis for crypto wallets

## Quick start

Requires .NET 10 SDK and Docker.

```powershell
dotnet run --project src/Hosting/Roamint.AppHost --launch-profile http
```

The Aspire dashboard URL appears in stdout (e.g. `http://localhost:15043/login?t=...`). Open it to see Postgres, PgAdmin, and the Web host with live state, logs, and OpenTelemetry traces.

First run pulls `pgvector/pgvector:pg17` and `dpage/pgadmin4`. Postgres data persists in the `roamint-pgdata` Docker volume across AppHost restarts.

## Tech stack

| Layer                | Choice                                                       |
|----------------------|--------------------------------------------------------------|
| Runtime              | .NET 10, C# 14                                               |
| Web UI               | Blazor Server (`Roamint.Web`)                                |
| Orchestration (dev)  | .NET Aspire 13.3.2                                           |
| Database             | PostgreSQL 17 + `pgvector`                                   |
| ORM                  | EF Core 10 (writes), Dapper (reads)                          |
| Tests                | xUnit, Shouldly, NSubstitute, Testcontainers.PostgreSql      |
| Telemetry            | OpenTelemetry (logs, metrics, traces via OTLP)               |
| LLM abstraction      | Microsoft.Extensions.AI (OpenAI + Anthropic behind it)       |
| Dispatcher           | Hand-rolled in-process; no MediatR                           |

## Architecture in one paragraph

Modular monolith with bounded contexts (Expenses, Wallet, Receipts, Insights). Each module owns its own DB schema. No foreign keys cross schemas. Cross-module talk happens via integration events through a transactional outbox. Inside each module: Domain → Application → Infrastructure → Contracts, with the web host only depending on `Contracts`.

## Docs

- [`docs/`](docs) — feature designs, decisions, and architecture notes (`docs/features/<NNNN>-<slug>.md`).
- [`CLAUDE.md`](CLAUDE.md) — operating instructions for Claude Code working in this repo.

## Hosting & cost

- Local dev: Aspire AppHost + Docker.
- Target production: containerized .NET app on Render free tier + Neon Postgres free tier. Target running cost: $0/month, with the option to upgrade to ~$5/month if cold starts get annoying.

## License

TBD.
