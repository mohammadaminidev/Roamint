# 0016 — Receipt item matching: learned Armenian→Farsi mappings via embeddings

## Context

Builds on 0015. Receipt OCR yields **Armenian** item names; the user's expense vocabulary
(and all historical data) is **Farsi**. The user wants:

1. The **first** time an Armenian item is seen, the app shows a **Farsi translation** to
   review; the user may rename it and set its unit, then it is saved.
2. The app **remembers** that mapping. The **next** time the same item appears — even if the
   OCR text differs slightly between scans — it shows the stored **final** Farsi name + unit
   automatically, no re-translation.
3. Deciding whether a receipt line is **new** or **something already seen** must use
   **semantic similarity (embeddings)**, *not* exact/prefix/contains matching — OCR text
   drifts between receipts and product varieties differ, so lexical keys break.

This is the learned-mapping + embedding layer over the 0015 receipt flow. It also lays the
first piece of vector search in the repo (pgvector is already the Postgres image), which the
V2 Insights module will later build on.

## Decisions

- **Recognition is by embedding nearest-neighbour, never lexical.** Each learned mapping
  stores an embedding of its Armenian source text; a new line is classified by cosine
  distance to the stored vectors (pgvector `<=>`). A single distance **threshold** decides
  known-vs-new (tuned empirically). Exact/`LIKE` matching is explicitly rejected.
- **Embed the Armenian source text** (recognition is Armenian↔Armenian — language-consistent
  and independent of translation quality). This is what lets the *same* item re-resolve
  without re-translating: a repeat sighting is a vector hit, so no Gemini call is needed.
- **Translate only on first sight.** Gemini (the 0015 OCR provider) translates Armenian→Farsi
  **only when a line is unrecognized** (a vector miss). Recognized lines reuse the stored
  final Farsi + unit.
- **Learn on confirm.** When the user confirms a receipt-sourced expense, upsert one mapping
  per line: `Armenian source text (+ embedding) → final Farsi description + unit + category`.
  A rename overwrites the prior value. Corrections train the system; over time the vector
  store handles nearly everything deterministically and embeddings only carry the cold start.
- **Lives in the Expenses module** (`expenses` schema). Expenses owns item vocabulary,
  categories, and units; the mapping's *value* is all Expenses concepts. The **key is an
  opaque external string** — Expenses exposes "resolve/learn an item from external text,"
  staying ignorant of Receipts. Receipts stays ignorant of Expenses (the host orchestrates),
  so no cross-schema FK and no module dependency.
- **Embedding model: local `bge-m3` via Ollama**, behind an `IItemEmbedder` port. Strongly
  multilingual (incl. Armenian + Persian), private, free, and Ollama is already installed.
  Gemini's embedding endpoint is a config-swappable alternative (same port). Model + endpoint
  are configuration, mirroring 0015's OCR provider.
- **pgvector access via Dapper raw SQL** for the nearest-neighbour read (`<=>` operator with
  an `ivfflat`/`hnsw` index), consistent with "Dapper for reads." The mapping is written via
  EF. The `vector` extension is enabled in a migration (the `pgvector/pgvector` image ships
  it). Binding uses the `Pgvector` NuGet type (version to verify before pinning).

## Domain / persistence model

**`ItemMapping`** (aggregate, `expenses.item_mappings`) — a learned resolution of external
receipt text to a canonical expense item.

- `ItemMappingId` (Guid v7).
- `SourceText` (string) — the Armenian text as read (normalized: trimmed, collapsed
  whitespace). Stored for the record and for re-learning; **not** the match key on its own.
- `CanonicalDescription` (string) — the user's final Farsi name.
- `Unit` (canonical code `g`/`ml`/`Pcs`, or null).
- `CategoryId` (Guid) — the category chosen for it.
- `LastUsedOnUtc` (timestamp).
- **Embedding** — a `vector` column populated by Infrastructure from `SourceText` via
  `IItemEmbedder`. The domain aggregate does **not** hold the float vector; it is an
  infrastructure shadow/owned column (the domain knows the semantic fields, not the maths).
- Invariants: non-empty `SourceText` and `CanonicalDescription`; `CategoryId` must be a real
  category (validated in the handler, like `RecordExpense`). Upsert is keyed on normalized
  `SourceText` (latest wins) — the same source re-confirmed updates in place.

No domain events needed yet (no consumer); the outbox symmetry can be added if Insights later
wants mapping changes.

## Application (Expenses, new slices)

- `MatchItem.cs` — query `MatchItem(string sourceText) : IQuery<ItemMatchView?>`. Handler
  (Infrastructure read model): embed `sourceText` via `IItemEmbedder`, nearest-neighbour over
  `item_mappings` within the distance threshold; return the mapping's
  `(CanonicalDescription, Unit, CategoryId)` or null. Dapper + pgvector.
- `RecordItemMapping.cs` — command
  `RecordItemMapping(string SourceText, string CanonicalDescription, string? Unit, CategoryId CategoryId)`.
  Validates the category, upserts the mapping (by normalized `SourceText`), recomputes the
  embedding. EF write.
- Port (`Ports.cs`): `IItemEmbedder.EmbedAsync(string text, CancellationToken) → float[]`.

## Public surface (Expenses Contracts)

- `IExpensesQueries.MatchItemAsync(string sourceText, CancellationToken)` →
  `ItemMatchDto?(string CanonicalDescription, string? Unit, Guid CategoryId)`.
- `IExpensesApi.RecordItemMappingAsync(RecordItemMappingRequest, CancellationToken)`,
  `RecordItemMappingRequest(string SourceText, string CanonicalDescription, string? Unit, Guid CategoryId)`.

## Receipts + host flow

- **Receipts OCR** returns, per line, the **Armenian `SourceText`** *and* a **Farsi
  translation** (used only when needed) plus amount/qty. `ParsedLine`/`ParsedLineDto` gain
  `SourceText` (the original Armenian); `Description` carries the Farsi translation.
- **Host (`/receipts/new` → `/add`)**: for each line, call
  `IExpensesQueries.MatchItemAsync(SourceText)`:
  - **hit** → seed the form line with the mapping's final Farsi description + unit + category
    (the "final mapping" shown next time).
  - **miss** → seed with the Gemini Farsi translation; category/unit left for the user.
  - The handoff carries each line's `SourceText` through to the form.
- **On confirm** (in `AddExpense`): after `RecordExpenseAsync`, for each receipt-sourced line
  call `RecordItemMappingAsync(SourceText, finalDescription, unit, categoryId)` to learn the
  (possibly renamed) mapping. Best-effort; a failure doesn't block the recorded expense.

## Packages to pin

- `Pgvector` (vector type binding for Npgsql/Dapper) — version to verify via
  `dotnet package search`.
- Embeddings reuse `Microsoft.Extensions.AI` (already pinned in 0015); the local Ollama
  embedder calls its OpenAI-compatible `/v1/embeddings`, or uses the native embeddings API.
- New Ollama model to pull: `bge-m3` (~1.2 GB).

## Open questions

- **Historical-Farsi bootstrap.** Recognition embeds Armenian, while the *existing* expense
  vocabulary is Farsi. The bge-m3 smoke test (below) showed cross-lingual same-item similarity
  (`Պանիր`↔`پنیر` ≈ 0.64) sits clearly above different-item noise (≈ 0.39), so snapping a
  first-ever Armenian item directly to the historical **Farsi** vocabulary is viable — embed
  the Armenian text once and search both the learned mappings *and* the (embedded) historical
  Farsi vocabulary with a slightly lower threshold (~0.55). Upgraded from "deferred" to a real
  phase-2 option; the core flow still works without it (user renames the first sighting).
- **Distance threshold** — empirically (bge-m3, cosine **similarity**): same item across OCR
  drift ≈ 0.75; variety↔generic ≈ 0.61; different items ≈ 0.30–0.39; cross-lingual same item
  ≈ 0.55–0.64. **Start threshold ≈ 0.65 similarity** (≈ 0.35 cosine *distance*) for
  Armenian↔Armenian recognition; ~0.55 if also matching Farsi history. bge-m3 compresses
  scores, so this is tuned, not generous — the learning loop + review screen absorb
  near-boundary errors. Index type (`hnsw` vs `ivfflat`): skip the index initially (tiny
  table), add when it matters.
- **Normalization** of `SourceText` before embedding (case, Armenian punctuation, the `(0406)`
  product codes on receipts) — strip codes? Decide during implementation with real data.
- **`SourceText` carried through Receipts** means the receipt DTO grows an Armenian field that
  is meaningless to Receipts itself — acceptable (opaque passthrough), but worth noting.

## Verification

- **Unit (Domain):** `ItemMapping` invariants (empty text rejected; upsert/rename overwrites).
- **Application:** `MatchItem` with a fake `IItemEmbedder` returning controlled vectors →
  returns the nearest mapping within threshold, null beyond it. `RecordItemMapping` upserts
  and re-embeds; unknown category rejected.
- **Infrastructure (Testcontainers + pgvector):** store two mappings with known vectors, query
  a third vector, assert the correct nearest neighbour and threshold cutoff; rename upsert
  replaces in place.
- **End-to-end (manual):** scan a receipt twice with slightly different framing → first sight
  shows Farsi translation; after confirming with a rename + unit, the second scan shows the
  renamed final mapping automatically (proving embedding recognition, not lexical).
- **Embedding smoke test:** before building, embed a few Armenian receipt items + their Farsi
  equivalents with `bge-m3` and eyeball that same-item pairs are closer than different-item
  pairs (the same empirical gate used for OCR in 0015).
