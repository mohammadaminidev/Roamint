# Roamint Docs

Every feature, module, and architectural decision is captured here as a markdown document.

## Conventions

- One file per feature under `features/`, numbered `NNNN-slug.md` (zero-padded to 4 digits).
- File structure: **Context → Decisions → Domain model (when applicable) → Public surface → Open questions → Verification.**
- The doc is written *before* the code. Plan files (under `~/.claude/plans/`) capture the per-session implementation plan; this `docs/` tree is the durable record.
- When a feature lands, tick its box in the README's V1 checklist and link the feature doc from the README's status section if it's a milestone.

## Index

### Features

| #    | Title                                                                       | Status      |
|------|-----------------------------------------------------------------------------|-------------|
| 0001 | [Solution scaffold](features/0001-solution-scaffold.md)                     | Done        |
| 0002 | [Expenses module — domain layer](features/0002-expenses-domain.md)          | Done        |
| 0003 | [In-process dispatcher](features/0003-dispatcher.md)                        | Done        |
| 0004 | [Expenses module — application layer](features/0004-expenses-application.md)| Done        |

### Modules

- **Expenses** — domain (0002), dispatcher (0003), and application layer (0004) landed; persistence + outbox slice next.
- **Wallet, Receipts, Insights** — not yet started. Each will get a `features/NNNN-<module>-...md` design doc before any code lands.

### Architecture decisions

_(None promoted to an ADR yet. Load-bearing decisions live in the relevant feature doc's **Decisions** section until they need to be referenced across multiple features.)_
