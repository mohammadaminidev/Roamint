using System.Globalization;
using System.Text;
using Roamint.Expenses.Contracts;

namespace Roamint.Web.Import;

/// <summary>
/// Dev-only one-off importer (feature 0013) that replays the user's expense spreadsheet (exported as CSV)
/// into the app through the public Expenses write API — full invariants, domain events, and outbox. The
/// sheet is month-bucketed with no day, all amounts in AMD; each (month × category) becomes one multi-line
/// expense dated to the 1st of its month. Idempotent: a no-op once any expenses exist. See
/// <c>docs/features/0013-spreadsheet-import.md</c>.
/// </summary>
public sealed class SpreadsheetImporter(IExpensesApi api, IExpensesQueries queries)
{
    private const string Currency = "AMD";

    // Spreadsheet category (Persian) → app category (English), with the optional Groceries tier tag (0013 D5/D9).
    // Persian/Arabic-Indic digits in the food-level labels are normalized to ASCII before lookup (Normalize).
    private static readonly Dictionary<string, (string Category, string? Tier)> CategoryMap = new(StringComparer.Ordinal)
    {
        ["وسایل خونه"] = ("Household", null),
        ["خوراکی لول 1"] = ("Groceries", "Essential"),
        ["خوراکی لول 2"] = ("Groceries", "Comfort"),
        ["خوراکی لول 3"] = ("Groceries", "Treat"),
        ["بهداشتی"] = ("Personal Care", null),
        ["غذا بیرون"] = ("Dining Out", null),
        ["حمل و نقل"] = ("Transport", null),
        ["سفر"] = ("Travel", null),
        ["متفرقه"] = ("Miscellaneous", null),
        ["مسکن"] = ("Rent", null),
        ["قبض"] = ("Utilities", null),
    };

    /// <summary>Reads the CSV from <paramref name="csv"/> and records the history under <paramref name="year"/>
    /// (the sheet has no year). Returns a summary; throws nothing for row-level problems — those surface as
    /// warnings so a partial import still completes.</summary>
    public async Task<ImportResult> ImportAsync(Stream csv, int year, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(csv);

        // Idempotency guard (0013 D8): never double-import on top of existing data.
        var existing = await queries.ListExpensesAsync(ct: ct);
        if (existing.Count > 0)
        {
            return ImportResult.NoOp(existing.Count);
        }

        using var reader = new StreamReader(csv, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct);

        var warnings = new List<string>();
        var parsed = Parse(text, warnings);
        if (parsed.Count == 0)
        {
            return new ImportResult(false, 0, 0, 0, 0, [], warnings);
        }

        var (categoryIds, categoriesCreated) = await EnsureCategoriesAsync(parsed, ct);
        var (tierTagIds, tagsCreated) = await EnsureTierTagsAsync(parsed, categoryIds, ct);

        // Group lines into one expense per (month × category), preserving first-seen order (0013 D4).
        var groups = new List<((int Month, string Category) Key, List<ExpenseLineDto> Lines)>();
        var index = new Dictionary<(int, string), int>();
        foreach (var line in parsed)
        {
            var key = (line.Month, line.Category);
            if (!index.TryGetValue(key, out var at))
            {
                at = groups.Count;
                index[key] = at;
                groups.Add((key, []));
            }

            IReadOnlyList<Guid>? tags = line.Tier is not null && tierTagIds.TryGetValue(line.Tier, out var tagId)
                ? [tagId]
                : null;

            groups[at].Lines.Add(new ExpenseLineDto(
                line.Description, categoryIds[line.Category], line.Amount, TagIds: tags));
        }

        var months = new Dictionary<int, (int Expenses, int Lines, decimal Total)>();
        var expensesRecorded = 0;
        var linesRecorded = 0;
        foreach (var (key, lines) in groups)
        {
            var occurredOn = new DateOnly(year, key.Month, 1);
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(key.Month);
            var note = $"Imported · {monthName} {year}";
            try
            {
                await api.RecordExpenseAsync(new RecordExpenseRequest(Currency, occurredOn, lines, note), ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                warnings.Add($"{monthName} / {key.Category}: skipped ({ex.Message})");
                continue;
            }

            expensesRecorded++;
            linesRecorded += lines.Count;
            var total = lines.Sum(l => l.Amount);
            var agg = months.GetValueOrDefault(key.Month);
            months[key.Month] = (agg.Expenses + 1, agg.Lines + lines.Count, agg.Total + total);
        }

        var monthSummaries = months
            .OrderBy(kv => kv.Key)
            .Select(kv => new MonthImport(
                CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(kv.Key), kv.Value.Expenses, kv.Value.Lines, kv.Value.Total))
            .ToList();

        return new ImportResult(
            false, categoriesCreated, tagsCreated, expensesRecorded, linesRecorded, monthSummaries, warnings);
    }

    private async Task<(Dictionary<string, Guid> Ids, int Created)> EnsureCategoriesAsync(
        IReadOnlyList<ParsedLine> parsed, CancellationToken ct)
    {
        var existing = await queries.ListActiveCategoriesAsync(ct);
        var ids = existing.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        foreach (var name in parsed.Select(l => l.Category).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!ids.ContainsKey(name))
            {
                ids[name] = await api.CreateCategoryAsync(new CreateCategoryRequest(name), ct);
                created++;
            }
        }

        return (ids, created);
    }

    private async Task<(Dictionary<string, Guid> Ids, int Created)> EnsureTierTagsAsync(
        IReadOnlyList<ParsedLine> parsed, Dictionary<string, Guid> categoryIds, CancellationToken ct)
    {
        var tiers = parsed.Where(l => l.Tier is not null).Select(l => l.Tier!).Distinct(StringComparer.Ordinal).ToList();
        if (tiers.Count == 0 || !categoryIds.TryGetValue("Groceries", out var groceriesId))
        {
            return (new Dictionary<string, Guid>(StringComparer.Ordinal), 0);
        }

        var existing = await queries.ListTagsAsync(groceriesId, ct);
        var ids = existing.ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        foreach (var tier in tiers)
        {
            if (!ids.ContainsKey(tier))
            {
                // CreateTag is idempotent on the active (name, scope) pair, so this is safe even if the
                // tier tag was already seeded under a different listing.
                ids[tier] = await api.CreateTagAsync(new CreateTagRequest(tier, groceriesId), ct);
                created++;
            }
        }

        return (ids.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal), created);
    }

    // Walks the parsed CSV rows into typed lines, tracking the current month from the marker rows. Rows before
    // the first marker belong to March (0013 D3). Unmappable categories and bad amounts become warnings.
    private static List<ParsedLine> Parse(string text, List<string> warnings)
    {
        var rows = ParseCsv(text);
        if (rows.Count == 0)
        {
            warnings.Add("The file is empty.");
            return [];
        }

        var header = rows[0];
        int item = IndexOf(header, "item"), type = IndexOf(header, "type"), price = IndexOf(header, "price");
        if (item < 0 || type < 0 || price < 0)
        {
            warnings.Add("Missing required columns — expected headers item, type, price.");
            return [];
        }

        var lines = new List<ParsedLine>();
        var currentMonth = 3; // March: the implicit first section before any month marker.
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var itemCell = Cell(row, item);
            var typeCell = Cell(row, type);

            if (string.IsNullOrWhiteSpace(itemCell) && string.IsNullOrWhiteSpace(typeCell))
            {
                continue; // blank spacer row.
            }

            // Month-marker row: a lone month name with no category.
            if (string.IsNullOrWhiteSpace(typeCell) && TryMonth(itemCell, out var marker))
            {
                currentMonth = marker;
                continue;
            }

            if (!CategoryMap.TryGetValue(Normalize(typeCell), out var mapped))
            {
                warnings.Add($"Row {r + 1}: unknown category '{typeCell}' — skipped.");
                continue;
            }

            if (!TryAmount(Cell(row, price), out var amount))
            {
                warnings.Add($"Row {r + 1} ('{itemCell}'): unreadable price '{Cell(row, price)}' — skipped.");
                continue;
            }

            lines.Add(new ParsedLine(currentMonth, mapped.Category, mapped.Tier, itemCell.Trim(), amount));
        }

        return lines;
    }

    private static bool TryAmount(string raw, out decimal value) =>
        decimal.TryParse(raw.Replace(",", string.Empty, StringComparison.Ordinal).Trim(),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0m;

    private static bool TryMonth(string raw, out int month)
    {
        var formats = new[] { "MMMM", "MMM" };
        if (DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            month = dt.Month;
            return true;
        }

        month = 0;
        return false;
    }

    // Folds Persian (۰-۹) and Arabic-Indic (٠-٩) digits to ASCII and trims, so "خوراکی لول ۱" matches the map.
    private static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            sb.Append(ch switch
            {
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                _ => ch,
            });
        }

        return sb.ToString();
    }

    private static int IndexOf(string[] header, string name) =>
        Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] : string.Empty;

    // Minimal RFC-4180 reader: handles quoted fields with embedded commas and doubled quotes. The real export
    // carries extra trailing columns (the rollup block) — harmless, since lines are read by header name.
    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var sawContent = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { inQuotes = false; }
                }
                else { field.Append(c); }

                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; sawContent = true; break;
                case ',': fields.Add(field.ToString()); field.Clear(); sawContent = true; break;
                case '\r': break;
                case '\n':
                    fields.Add(field.ToString());
                    rows.Add([.. fields]);
                    fields.Clear();
                    field.Clear();
                    sawContent = false;
                    break;
                default: field.Append(c); sawContent = true; break;
            }
        }

        if (sawContent || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        return rows;
    }

    private sealed record ParsedLine(int Month, string Category, string? Tier, string Description, decimal Amount);
}

/// <summary>Outcome of an import run, for the page to render.</summary>
public sealed record ImportResult(
    bool AlreadyImported,
    int CategoriesCreated,
    int TagsCreated,
    int ExpensesRecorded,
    int LinesRecorded,
    IReadOnlyList<MonthImport> Months,
    IReadOnlyList<string> Warnings)
{
    public static ImportResult NoOp(int existing) =>
        new(true, 0, 0, 0, 0, [], [$"{existing} expense(s) already exist — import skipped. Reset the database to re-import."]);
}

/// <summary>Per-month tally of what was recorded.</summary>
public sealed record MonthImport(string Month, int Expenses, int Lines, decimal TotalAmd);
