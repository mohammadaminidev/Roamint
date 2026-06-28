# Roamint

Personal multi-currency expense tracker for daily use by an expat in Armenia. Multi-currency by necessity (cash USD, cash AMD, USDT), single-tenant by design.

Also a portfolio piece — modular monolith on .NET 10 with .NET Aspire, Blazor Server, PostgreSQL + pgvector, OpenTelemetry, and DDD-style bounded contexts.

> **Privacy:** This repo is public. The real data is not. Seed values and screenshots use anonymized data. The schema is single-tenant and not hardened for multi-user.

## Status

**V1 in progress.** Expenses (capture, per-line categories, tags, converting list, monthly-summary reports home) and Wallet (exchange rates + display conversion) are shipped; category management and Wallet balances remain. Aspire AppHost orchestrates Postgres (`pgvector/pgvector:pg17`), PgAdmin, and the Blazor Server host. Build is clean, dashboard goes green.

See [`docs/features/`](docs/features) for per-slice design records (`0001`–`0012`).

### V1 roadmap — replace the Excel workflow

- [x] Solution scaffold
- [x] Expenses module — domain (Expense, Category) with strongly-typed IDs, EF Core persistence, outbox-backed events (capture done; drain lands with the first consumer)
- [x] Add-expense form — mobile-first, one-handed thumb input; per-item categories + optional quantities (g/ml/Pcs), derived total, item autocomplete carrying category + unit + tags, category suggestion, duplicate-guard confirmation on a same-day/-currency/-total twin (0022)
- [x] Tags — scoped (per-category) + global classification axis; managed vocabulary with per-line picker and inline create
- [x] Exchange rates — `Exchange` + `ExchangeRate` aggregates; USD-triangulated display conversion; manual override
- [x] Expense list — date-range / category filters; display-denomination conversion ("All" = originals) with missing-rate routing to the rate page
- [x] Monthly summary — reports home (`/`) with per-category totals in AMD / USD / IRT (toman), prior-month delta, and missing-rate routing to the rate page
- [ ] Category management — create, rename, archive, merge (no destructive delete)

### V2 — in progress

- [x] Receipt photo OCR (Receipts module) — photograph a receipt, OCR extracts
  items/prices via Gemini 2.5 Flash (reads Armenian), review + assign categories in the
  add-expense form, link to the recorded expense. Config-driven provider over an
  OpenAI-compatible endpoint; stub fallback when no key. See `docs/features/0015`.
- [x] Receipt item matching (Expenses) — receipt items are translated to Farsi and matched
  to your vocabulary by embedding similarity (local `bge-m3` + pgvector); a confirmed rename
  is learned, so the next receipt resolves the item (name + unit + category) automatically.
  See `docs/features/0016`.
- [x] Clipboard order import (Receipts module) — paste a Yerevan City order's JSON, a
  source-agnostic parser registry maps it to the same review draft as OCR, reusing item
  matching + learn-on-confirm. Dedup on `(source, external id)`. See `docs/features/0017`.
- [ ] Natural-language queries over expenses (Insights module) — the feature that
  motivates the outbox drain.
- [ ] Spreadsheet export (import landed dev-only in 0013).

### V3+ — later

- Wallet balances — de-emphasized; per-currency balances from exchanges + expenses (builds the outbox drain)
- Anomaly / budget alerts
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
