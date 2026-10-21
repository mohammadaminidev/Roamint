# Roamint Docs

Every feature, module, and architectural decision is captured here as a markdown document.

## Conventions

- One file per feature under `features/`, numbered `NNNN-slug.md` (zero-padded to 4 digits).
- File structure: **Context → Decisions → Domain model (when applicable) → Public surface → Open questions → Verification.**
- The doc is written *before* the code. Plan files (under `~/.claude/plans/`) capture the per-session implementation plan; this `docs/` tree is the durable record.
- When a feature lands, tick its box in the README's V1 checklist and link the feature doc from the README's status section if it's a milestone.

## Index

### Features

| #    | Title |
|------|-------|
| 0001 | [Solution scaffold](features/0001-solution-scaffold.md) |
| 0002 | [Expenses module: domain layer](features/0002-expenses-domain.md) |
| 0003 | [In-process dispatcher (CQS + events)](features/0003-dispatcher.md) |
| 0004 | [Expenses module: application layer](features/0004-expenses-application.md) |
| 0005 | [Expenses module: Infrastructure + Contracts](features/0005-expenses-infrastructure-contracts.md) |
| 0006 | [Expenses module: add-expense form (read side + Blazor UI)](features/0006-add-expense-form.md) |
| 0007 | [Expenses module: line items, item autocomplete, category suggestion](features/0007-line-items-autocomplete-suggestion.md) |
| 0008 | [Expenses module: line-level categories + quantities](features/0008-line-level-categories.md) |
| 0009 | [Expenses module: tags (scoped + global)](features/0009-tags.md) |
| 0010 | [Wallet module: exchange rates (display-conversion enabler)](features/0010-wallet-rates.md) |
| 0011 | [Converting expense list + exchange-rate page](features/0011-converting-expense-list.md) |
| 0012 | [Monthly summary (reports home)](features/0012-monthly-summary.md) |
| 0013 | [Spreadsheet import (historical seed)](features/0013-spreadsheet-import.md) |
| 0014 | [Phone / LAN access to the dev app](features/0014-phone-lan-access.md) |
| 0015 | [Receipts: photo OCR → pre-filled expense](features/0015-receipts-ocr.md) |
| 0016 | [Receipt item matching: learned Armenian→Farsi mappings via embeddings](features/0016-receipt-item-matching.md) |
| 0017 | [Clipboard order import: paste JSON → pre-filled expense](features/0017-clipboard-order-import.md) |
| 0018 | [Tailscale HTTPS + live camera with token-capped capture](features/0018-https-live-camera.md) |
| 0019 | [Background receipt processing with a live notification bar](features/0019-background-receipt-processing.md) |
| 0020 | [Installable PWA + offline notice](features/0020-installable-pwa.md) |
| 0021 | [Exact item matching: drop semantic recognition, exact-or-manual](features/0021-uncertain-item-matching.md) |
| 0022 | [Duplicate-expense guard](features/0022-duplicate-expense-guard.md) |

Status per feature lives in the root README's roadmap.

### Modules

- **Expenses** — domain, application, persistence, UI (0002–0009, 0011–0013, 0016, 0021, 0022).
- **Wallet** — exchange rates + display conversion (0010, 0011). Balances are V3+.
- **Receipts** — OCR, clipboard import, background processing (0015, 0017, 0019).
- **Insights** — planned (V2); no design doc or code yet.

### Architecture decisions

_(None promoted to an ADR yet. Load-bearing decisions live in the relevant feature doc's **Decisions** section until they need to be referenced across multiple features.)_
