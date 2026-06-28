# 0021 — Exact item matching: drop semantic recognition, exact-or-manual

## Context

0016 resolves an Armenian receipt line to a learned Farsi item by **embedding
nearest-neighbour** (bge-m3, cosine distance via pgvector) under a single similarity threshold.
In real use this produces confident wrong answers: a photographed لیمو (lemon) resolves to
هویج (carrot), گوشت چرخ کرده (ground meat) to پیاز (onion). bge-m3 compresses scores, short
Armenian grocery names sit in a narrow band, and any single threshold either waves through
wrong neighbours or rejects real repeats. The background worker (0019) then **silently
overwrites** the translation with the wrong match before the user sees it.

The user's decision after seeing this: **don't guess.** Recognise an item only when the source
text was seen **exactly** before; otherwise show the translation and let the user set it by
hand. Exact-or-manual has **zero false positives** — لیمو can never become هویج because nothing
fuzzy ever fires. This deliberately reverses 0016's "recognition by embedding, never lexical"
decision, grounded in the observed failure: for this single-user app, a missed repeat (re-set
manually) is far cheaper than a confident wrong category.

## Decisions

- **Recognition is exact lexical match on the normalised source text.** `MatchItem` looks up
  the stored mapping whose normalised `SourceText` equals the query's, and returns it or null.
  No embedding read, no pgvector, no distance, no threshold on the read path.
- **Miss → translation, set manually.** Unchanged from today's miss behaviour: the line keeps
  the Gemini Farsi translation as its description and the user assigns category/unit. ("Give me
  the translation of the non-English text to set manually.")
- **Stronger shared normalisation as the key.** Exact matching makes formatting noise matter, so
  `ItemMapping.Normalize` (already shared by the write-side dedup key and — until now — the
  embedder) gains **lowercase-invariant** on top of trim + whitespace-collapse. Write and read
  call the same function, so the stored key and the lookup key stay identical. Stripping the
  `(0406)`-style product codes 0016 flagged is **deferred** — codes may distinguish real
  varieties, and aggressive stripping reintroduces unwanted merges; revisit only if drift on
  the same item proves common in the log.
- **Embeddings kept dormant, not removed.** `RecordItemMapping` still computes and stores the
  embedding on confirm (the Ollama/bge-m3 write path and the `embedding` column stay), so
  Insights V2 can reuse the vectors without a re-embed. Nothing *reads* them after this change.
  The `IItemEmbedder` port and the column survive; only the match handler stops depending on
  them.
- **No contract or host change.** `ItemMatchDto(CanonicalDescription, Unit, CategoryId)` is
  unchanged — a hit or null is all the worker ever needed. `ReceiptProcessingWorker`,
  `ImportOrder.razor`, and `AddExpense.razor` already do "hit → apply, miss → keep translation,"
  so they are untouched. The earlier 0021 "confidence band / did-you-mean pick-list" idea is
  abandoned; exact-or-manual makes it unnecessary.

## Implementation surface

- **`MatchItemHandler`** (`ReadModels.cs`, Infrastructure): replace the embed + `<=>` pgvector
  nearest-neighbour with a plain EF exact lookup (`Where(m => m.SourceText == normalized)`,
  `FirstOrDefault`) keyed on `ItemMapping.Normalize(query.SourceText)`, mirroring the other
  simple read models. Drop the `IItemEmbedder` dependency, the `MaxDistance` constant, and the
  raw-`NpgsqlConnection`/`Pgvector` code. A too-long line (can't have been stored) is a miss,
  not a throw. No hit/miss log — exact matching has no threshold to calibrate, so it would be
  pure CA1848 boilerplate.
- **`ItemMapping.Normalize`**: add `ToLowerInvariant()` after whitespace-collapse. Update its
  comment (no longer "the text handed to the embedder").
- **Doc-comment cleanup** on `MatchItem`, `IItemMappingRepository`, and `ItemMapping` that
  currently assert "recognition is by embedding" → "exact normalised key; embeddings retained
  only for Insights."
- **`RecordItemMapping`, the embedding column, `IItemEmbedder`, Ollama wiring**: unchanged.

## Open questions

- **Existing stored keys** were normalised without `ToLowerInvariant`; after this change a
  read lowercases and may miss a pre-existing mixed-case row until it is re-confirmed. Tiny
  data set, acceptable one-time cost — no backfill migration.
- **Product-code stripping** (`(0406)`) left out on purpose; the hit/miss log tells us whether
  same-item drift is frequent enough to justify it.

## Verification

- **Infrastructure (Testcontainers):** store a mapping; query the identical text (and a
  case/whitespace variant) → returns it; query different text → null. No embedder involved.
- **Host (manual):** scan a never-seen item → review form shows the Gemini translation, no
  category, nothing auto-filled wrong. Confirm with a category, rescan the *same* receipt text
  → resolves to the saved mapping. Scan a different item → never picks up an unrelated mapping.
- **Regression:** `RecordItemMapping` still writes the embedding (column populated on confirm),
  confirming the dormant path is intact for Insights.
