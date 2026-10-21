# Screenshots

Images referenced from the root README. **Anonymized data only** — never capture the real
`roamint-pgdata` database.

| File                    | Screen                                         | Route                         | Viewport           |
|-------------------------|------------------------------------------------|-------------------------------|--------------------|
| `add-expense-mobile.png`| Add-expense form with 2–3 lines, categories set | `/add`                        | 390 × 844 (mobile) |
| `monthly-summary.png`   | Reports home: per-category totals + delta      | `/`                           | 390 × 844 (mobile) |
| `receipt-review.png`    | OCR review: draft lines pre-filled from a receipt | `/receipts/new` → `/add`    | 390 × 844 (mobile) |

Captured at 390 × 844, device scale 2, against a throwaway Postgres container (never the real
`roamint-pgdata` volume) seeded through `/import` with a made-up CSV. The receipt shot is a
synthetic Armenian receipt fed through headless Edge's fake camera and read by Gemini.

Known issue found while capturing: **Choose photo** on `/receipts/new` fails (`OnFile` sets
`busy` and re-renders, which removes the `<InputFile>` before its stream is read); the live
camera path works.
