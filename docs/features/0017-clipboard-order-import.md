# 0017 — Clipboard order import: paste JSON → pre-filled expense

## Context

A third capture channel, alongside manual entry and receipt OCR (0015). The user shops
online at **Yerevan City**; every order is already a clean, structured record in their
account. Photographing a screen or re-typing it would be wasteful when the source *is*
machine-readable. So: the user copies the order's JSON response from the browser/app and
pastes it into Roamint, which parses it into the **same `ParsedDraft`** the OCR produces
and drops it onto the **same review screen**. Everything downstream — item matching
(0016), category review, `RecordExpense`, learn-on-confirm — is reused unchanged.

This is **not** an HTTP integration. An earlier framing fetched orders directly from the
Yerevan City API with the user's bearer token. Rejected (see Decisions): copy-paste
deletes the token lifecycle, the order-discovery endpoint, the unofficial-API fragility,
and — since the repo is public — the problem of a live credential in config. The cost is
one paste per shopping trip, the same ergonomics as snapping a receipt.

The importer is **source-agnostic by construction**: a registry of small parsers, each
recognizing and mapping one known JSON shape. Yerevan City is the first (and, today,
only) concrete parser. Adding a second store is a one-class change, not a config edit.

## Decisions

- **Copy-paste, not an API client.** No stored bearer token (it expires ~quarterly and
  has no refresh flow in the observed payload; it must never sit in a public repo's
  config), no order-list endpoint to reverse-engineer, no unofficial-API breakage to
  babysit. A pure function of pasted text — deterministic, offline, testable.
- **A parser *registry*, not a mapping DSL.** The literal "map any JSON" idea is rejected
  as over-engineering: the app cannot know that `price` is the amount for an *arbitrary*
  unknown shape — a per-source mapping must exist somewhere. The only question is code vs.
  config; for a solo senior .NET dev, **code wins** (type-safe, testable, no
  mapping-interpreter to build and debug). The seam is `IOrderImportParser`
  (`CanParse` + `Parse`); the importer tries each parser, first structural match wins,
  none-match → a friendly "unrecognized source." Open/closed at the parser level, which is
  where the extensibility belongs — *not* a runtime-configurable field table for sources
  that don't exist yet.
- **LLM fallback is V2, explicitly deferred.** The disciplined answer to "truly *any*
  JSON" is not a config engine but an **LLM parser as the last entry in the chain**,
  reusing the `IChatClient` already standing in 0015: no structural parser matches → hand
  the raw JSON to Gemini with an "extract a `ParsedDraft`" prompt, review screen as the
  net. It costs one more parser class and zero new infrastructure — but **build it only
  when a second source is actually pasted.** Noted here so the seam is shaped to accept it
  (the registry already is); not implemented now.
- **Lives in the Receipts module** (`receipts` schema), as a sibling aggregate
  `ImportedOrder` next to `Receipt`, **sharing the existing `ParsedDraft` value object**.
  A new module would duplicate the DbContext, migration service, outbox, persistence
  anchor, and wiring for one small aggregate — over-engineering. The conceptual stretch
  ("Receipts" now also holds non-receipt captures) is acknowledged: the module is really
  *"external sources → drafts."* If more sources arrive later, revisit a rename or split;
  not now. The public surface gets its **own `IImportsApi`** in `Receipts.Contracts` so
  the host's import flow doesn't masquerade as receipt upload.
- **Dedup on `(Source, ExternalOrderId)`.** The order id (`AC0000122700`) is stable, so
  re-pasting the same order is caught — the handler returns `AlreadyImported` (with the
  existing expense id) instead of double-spending. Enforced by a unique index *and* a
  pre-check in the handler for the friendly path.
- **Currency hardcoded `AMD`; `paymentMethod` captured-but-unused.** `RecordExpenseRequest`
  is `(CurrencyCode, OccurredOn, Lines, Note)` — no wallet/payment field (balances are
  V3+). So `paymentMethod: 2` doesn't matter to V1; AMD is all the expense needs.
- **`price` is the per-line total; validate the sum.** Verified against the real sample:
  `Σ orderItems[].price = 6787.04 ≈ totalPrice 6787`, including a `quantity: 2` line whose
  `price` is the *line* total, not unit price. So **no qty×unit reconstruction** — the ~18
  other per-item fields are noise. The parser asserts `Σprice ≈ totalPrice` (small rounding
  tolerance) and warns on mismatch rather than silently importing a wrong total.
- **`codeSap` ignored in V1.** Each line carries a stable SKU (`codeSap`), a more reliable
  identity than embeddings for same-source repeats. But 0016's embedding match already
  recognizes the stable English `name` trivially (no OCR drift), so reuse it as-is; note
  `codeSap` as a future exact-match shortcut, don't build a second match path now.

## Domain model

**Aggregate: `ImportedOrder`** (`receipts.imported_orders`) — a pasted external order and
its link lifecycle. Mirrors `Receipt` minus the image and the OCR-failure state (parsing
is synchronous and total: a shape either matches a parser or is rejected before any
aggregate exists).

- `ImportedOrderId` — `record struct` over `Guid`, `New(TimeProvider)` → v7.
- `Source` (string) — the parser that handled it, e.g. `"YerevanCity"`.
- `ExternalOrderId` (string) — the source's own id (`data.orderId`). Dedup key with
  `Source`.
- `Draft` (`ParsedDraft`) — the shared value object from 0015.
- `Status`: `Imported → Linked`. `Linked` terminal.
- `LinkedExpenseId` (`Guid?`) — opaque, set on link (no cross-schema FK).
- `ImportedOnUtc` (`DateTimeOffset`).

**Invariants**

- Non-empty `Source` and `ExternalOrderId`; `Draft` has ≥ 1 line.
- `LinkTo(expenseId)` allowed only from `Imported`; rejects `Guid.Empty`; sets `Linked`.
- Unique `(Source, ExternalOrderId)` — re-import is caught in the handler; the DB unique
  index is the backstop.

**Domain events** (receipts outbox, write-only — symmetry, no drain, as in 0015/0016):
`OrderImported`, `ImportedOrderLinked`.

## Application (Receipts module)

Receipts now spans **two aggregates** (`Receipt`, `ImportedOrder`) → introduce per-aggregate
subfolders per the CLAUDE.md rule: `Receipts/` (existing `UploadReceipt`, `LinkReceipt`,
`GetReceipt`) and **`Imports/`** (new), with matching sub-namespaces.

- `Imports/ImportOrder.cs` — command `ImportOrder(string RawJson)`. Parse `RawJson` to a
  `JsonDocument`; ask each injected `IOrderImportParser` `CanParse` (first match wins);
  none → `UnrecognizedImportException`. Build `ParsedOrder(ExternalOrderId, ParsedDraft)`.
  Dedup: if `(Source, ExternalOrderId)` exists → return it (`AlreadyImported`, with its
  `LinkedExpenseId`); else create `ImportedOrder`, save, return id + source + draft.
- `Imports/LinkImportedOrder.cs` — command `(ImportedOrderId, Guid expenseId)`;
  `order.LinkTo(...)`, save. Called by the host after a successful `RecordExpense`.
- **Port** (`Ports.cs`): `IOrderImportParser` — `string Source { get; }`,
  `bool CanParse(JsonElement root)`, `ParsedOrder Parse(JsonElement root)`. The handler
  depends on `IEnumerable<IOrderImportParser>`.
- **Exceptions** (`Exceptions.cs`): `UnrecognizedImportException` (no parser matched),
  surfaced to the page as a friendly message — mirrors how the kernel `DomainException`
  surfaces today.

## Infrastructure

- `ImportedOrderConfiguration` — map `ImportedOrder`; reuse the `ParsedDraft`/`ParsedLine`
  mapping approach from `ReceiptConfiguration` (ValueConverter/owned collection, single
  `private ImportedOrder(ImportedOrderId)` ctor + backing fields, per the EF 10 traps).
  Unique index on `(Source, ExternalOrderId)`. New migration on `ReceiptsDbContext`.
- `YerevanCityOrderParser : IOrderImportParser` (`Source = "YerevanCity"`):
  - `CanParse`: structural fingerprint — `success` present **and** `data.orderItems` is an
    array **and** items carry `codeSap`/`isKilogram`. No token, no network.
  - `Parse` → `ParsedDraft` per the field map below; asserts `Σprice ≈ totalPrice`.
- `IItemRepository`-style read for the dedup pre-check lives with the other Receipts
  repositories; `ReceiptsPersistence` stays the dispatcher Infrastructure-scan anchor.

### Yerevan City field map (`data.*` → `ParsedDraft`)

| Target | Source | Note |
|---|---|---|
| `ExternalOrderId` | `orderId` | dedup key (`"AC0000122700"`) |
| `Draft.Date` | `createDate` | → `DateOnly` (`2026-06-21`) |
| `Draft.CurrencyCode` | — | constant `"AMD"` |
| `Draft.Merchant` | — | constant `"Yerevan City"` |
| line `Amount` | `orderItems[].price` | line total, already AMD |
| line `SourceText` | `orderItems[].name` | stable English key for 0016 matching |
| line `Description` | `orderItems[].name` | first sight shows English; user renames to Farsi once → learned. (Translation = LLM-fallback V2 concern.) |
| line `QuantityValue`/`Unit` | weight items (`isKilogram:true`) → `weight` g, unit `"g"`; count items → `quantity`, unit `"Pcs"` | package size (`weightMeasure` G/ML) is product metadata, **not** purchase unit; review fixes edge cases |

`codeSap`, `bonus`, `visiblePrice`, `weightStep`, `productPricePerUnit`, … are dropped.

## Public surface (`Roamint.Receipts.Contracts`)

- `IImportsApi`:
  - `Task<ImportOrderResult> ImportAsync(ImportOrderRequest request, CancellationToken)`
  - `Task LinkAsync(Guid importId, Guid expenseId, CancellationToken)`
- `ImportOrderRequest(string RawJson)`.
- `ImportOrderResult(Guid ImportId, string Source, bool AlreadyImported,
  Guid? ExistingExpenseId, ParsedDraftDto Draft)`.
- Reuses the existing `ParsedDraftDto` / `ParsedLineDto`.
- `ReceiptsModule.AddReceiptsModule` also registers `IImportsApi`, the parser(s)
  (`YerevanCityOrderParser`), and the new dispatcher slices.

## Host flow (`Roamint.Web`)

- New page `/import` (`ImportOrder.razor`): a `<textarea>` + "Import" button →
  `IImportsApi.ImportAsync(raw)`.
  - `AlreadyImported` → inline warning ("Imported already on … as expense …"), no nav.
  - `UnrecognizedImportException` → friendly "couldn't recognize this order" message.
  - success → for each line, `IExpensesQueries.MatchItemAsync(SourceText)` (the exact loop
    `ReceiptCapture.razor` already runs), build the seed, hand off, navigate `/add`.
- **Generalize the receipt handoff.** `ReceiptHandoff`/`ReceiptSeed`/`ReceiptLineSeed`
  become source-neutral (rename to `Draft*`/`CaptureSeed`) and carry a **link target** so
  `AddExpense`'s confirm calls the right linker — `IReceiptsApi.LinkAsync` for a receipt,
  `IImportsApi.LinkAsync` for an import. Small mechanical refactor; no behavior change for
  receipts.
- Entry point alongside "Scan receipt" in the add flow / `NavBar`.

## Packages

**None.** `System.Text.Json` is built in. The V2 LLM fallback would reuse
`Microsoft.Extensions.AI` (already pinned in 0015) — no new dependency then either.

## Implementation notes (deviations from the design above)

- **Application sub-namespace is `OrderImports`, not `Imports`.** `CA1716` (warnings-as-errors)
  rejects `Imports` — it collides with the VB `Imports` keyword. The folder/sub-namespace is
  `Roamint.Receipts.Application.OrderImports`; the receipt slices moved to `.Receipts` as planned.
- **The import page route is `/orders/new`, not `/import`.** `/import` is already taken by the
  dev-only spreadsheet seeder (0013). `/orders/new` also parallels `/receipts/new`.
- **The handoff generalization was a rename, not a parallel type.** `ReceiptHandoff`/`ReceiptSeed`/
  `ReceiptLineSeed` (namespace `Roamint.Web.Receipts`) became `CaptureHandoff`/`CaptureSeed`/
  `CaptureLineSeed` (namespace `Roamint.Web.Capture`), carrying a `CaptureSource` (`Receipt` |
  `ImportedOrder`) + `SourceId` link target. `AddExpense` switches on the source to call the right
  linker (`IReceiptsApi.LinkAsync` vs. `IImportsApi.LinkAsync`); learn-on-confirm runs for both.

## Open questions

- **Bonus / delivery divergence.** In the sample `deliveryPrice` and `usedBonusAmount` are
  `0`, so `totalPrice == totalToPay`. When bonus is redeemed they diverge — actual money
  spent is `totalToPay`, but item prices sum to `totalPrice`. Handle as an adjustment line
  ("Bonus redeemed", negative) reconciling to `totalToPay`, or record the gross. Decide
  with a real non-zero order; the integrity check should tolerate it rather than block.
- **Handoff rename scope.** `Receipt*` → `Draft*`/`Capture*` touches `ReceiptCapture.razor`
  and `AddExpense.razor`; confirm the rename vs. a thin parallel type during implementation.
- **`codeSap` exact-match path** — a future optimization to skip embeddings for same-source
  repeats; revisit if/when matching cost or accuracy warrants.
- **More sources** — if a second/third store lands, reconsider whether `ImportedOrder`
  deserves its own schema/module and whether the LLM fallback is worth building.

## Verification

- **Unit (Domain):** `ImportedOrder` invariants — empty `Source`/`ExternalOrderId`,
  empty draft, link-from-non-`Imported`, terminal `Linked`, `Guid.Empty` link rejected.
- **Unit (parser):** feed the real sample payload to `YerevanCityOrderParser` → 10 lines;
  amounts sum to `6787`(±rounding) and the `Σprice ≈ totalPrice` check passes; weight items
  map to grams (`Cherry` 722 g), count items to `Pcs` (`Eggs` 1, `Patties` 2); date
  `2026-06-21`; currency `AMD`. A non-Yerevan JSON → `CanParse` false.
- **Application:** `ImportOrder` over the sample → draft as above; a second identical
  import → `AlreadyImported`, no new aggregate; unrecognized JSON →
  `UnrecognizedImportException`.
- **Infrastructure (Testcontainers):** round-trip an `ImportedOrder` with a multi-line
  draft through `ReceiptsDbContext`; unique `(Source, ExternalOrderId)` enforced; outbox
  row written in the same transaction.
- **End-to-end (manual):** copy a real Yerevan City order JSON → `/import` → paste →
  review pre-filled (previously-seen items show their learned Farsi name + category) →
  assign the new ones → confirm → expense in the list, order `Linked`; re-paste the same
  order → "already imported" warning.
