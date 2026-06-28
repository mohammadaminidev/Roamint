using Microsoft.EntityFrameworkCore;
using Roamint.Application.Abstractions;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// Read model for the active-category list. Bypasses the aggregate and projects straight off the
/// <c>expenses.categories</c> table with <c>AsNoTracking</c> — EF for now; Dapper arrives with the
/// analytics screens.
/// </summary>
internal sealed class ListActiveCategoriesHandler(ExpensesDbContext db)
    : IQueryHandler<ListActiveCategories, IReadOnlyList<CategoryView>>
{
    public async Task<IReadOnlyList<CategoryView>> HandleAsync(ListActiveCategories query, CancellationToken ct)
    {
        // Project Id + Name (Id read back through its value converter), then map to the view client-side
        // so we don't ask the provider to translate CategoryId.Value into SQL.
        var rows = await db.Categories
            .AsNoTracking()
            .Where(category => category.Status == CategoryStatus.Active)
            .OrderBy(category => category.Name)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(ct);

        return rows.Select(row => new CategoryView(row.Id.Value, row.Name)).ToList();
    }
}

/// <summary>
/// Read model for the tag list. With no category filter it returns every active tag (management); with one
/// it returns the picker set for a line of that category — global tags ∪ tags scoped to it (0009 D4).
/// </summary>
internal sealed class ListTagsHandler(ExpensesDbContext db)
    : IQueryHandler<ListTags, IReadOnlyList<TagView>>
{
    // The grocery food-level tiers (0013 D9) are an ordered scale, not an alphabetical set — show them
    // least-to-most discretionary. Any tag outside the scale ranks after them, ordered by name.
    private static readonly string[] TierOrder = ["Essential", "Comfort", "Treat"];

    public async Task<IReadOnlyList<TagView>> HandleAsync(ListTags query, CancellationToken ct)
    {
        var tags = db.Tags.AsNoTracking().Where(tag => tag.Status == TagStatus.Active);

        if (query.CategoryFilter is Guid categoryId)
        {
            var scope = new CategoryId(categoryId);
            tags = tags.Where(tag => tag.Scope == null || tag.Scope == scope);
        }

        var rows = await tags
            .Select(tag => new { tag.Id, tag.Name, tag.Scope })
            .ToListAsync(ct);

        return rows
            .OrderBy(row => TierRank(row.Name))
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => new TagView(row.Id.Value, row.Name, row.Scope?.Value))
            .ToList();
    }

    private static int TierRank(string name)
    {
        var index = Array.IndexOf(TierOrder, name);
        return index < 0 ? int.MaxValue : index;
    }
}

/// <summary>
/// Autocomplete over item history. The corpus is the distinct line descriptions; each suggestion carries
/// the category and quantity unit last used with that text so the UI can fill the field, light the chip,
/// and pre-select the unit in one round trip. Filtering is an <c>ILIKE %text%</c> in SQL; prefix-vs-contains
/// ranking, case-insensitive de-dupe, and recency ordering happen in memory over the small matched set
/// (personal scale — a trigram index is a future concern, not built).
/// </summary>
internal sealed class SuggestItemsHandler(ExpensesDbContext db)
    : IQueryHandler<SuggestItems, IReadOnlyList<ItemSuggestionView>>
{
    public async Task<IReadOnlyList<ItemSuggestionView>> HandleAsync(SuggestItems query, CancellationToken ct)
    {
        var text = query.Prefix.Trim();
        if (text.Length == 0 || query.Limit <= 0)
        {
            return [];
        }

        var pattern = $"%{Like.Escape(text)}%";

        // Push the ILIKE inside the navigation so it translates over the line collection (0007 EF trap).
        var rows = await db.Expenses
            .AsNoTracking()
            .SelectMany(expense => expense.Lines
                .Where(line => EF.Functions.ILike(line.Description, pattern, Like.EscapeChar))
                .Select(line => new Candidate(
                    line.Description,
                    line.CategoryId,
                    line.Quantity != null ? line.Quantity.Unit : (QuantityUnit?)null,
                    expense.OccurredOn,
                    expense.Id,
                    line.Id)))
            .ToListAsync(ct);

        var winners = rows
            .GroupBy(candidate => candidate.Text, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var mostRecent = group
                    .OrderByDescending(candidate => candidate.OccurredOn)
                    .ThenByDescending(candidate => candidate.ExpenseId.Value)
                    .First();
                var isPrefix = mostRecent.Text.StartsWith(text, StringComparison.OrdinalIgnoreCase);
                return (mostRecent, isPrefix);
            })
            .OrderByDescending(entry => entry.isPrefix)
            .ThenByDescending(entry => entry.mostRecent.OccurredOn)
            .ThenByDescending(entry => entry.mostRecent.ExpenseId.Value)
            .Take(query.Limit)
            .Select(entry => entry.mostRecent)
            .ToList();

        var tagsByLine = await LoadTagsAsync(winners.Select(winner => winner.LineId).ToList(), ct);

        return winners
            .Select(winner => new ItemSuggestionView(
                winner.Text,
                winner.CategoryId.Value,
                winner.Unit?.ToCode(),
                tagsByLine.GetValueOrDefault(winner.LineId, [])))
            .ToList();
    }

    /// <summary>Loads the remembered tag ids for the winning lines in one round trip. Owned tag rows come
    /// along when the line entities are materialized, so we read them via the line's domain accessor.</summary>
    private async Task<Dictionary<ExpenseLineId, IReadOnlyList<Guid>>> LoadTagsAsync(
        List<ExpenseLineId> lineIds, CancellationToken ct)
    {
        if (lineIds.Count == 0)
        {
            return [];
        }

        var lines = await db.Expenses
            .AsNoTracking()
            .SelectMany(expense => expense.Lines)
            .Where(line => lineIds.Contains(line.Id))
            .ToListAsync(ct);

        return lines.ToDictionary(
            line => line.Id,
            line => (IReadOnlyList<Guid>)line.TagIds.Select(tagId => tagId.Value).ToList());
    }

    private readonly record struct Candidate(
        string Text, CategoryId CategoryId, QuantityUnit? Unit, DateOnly OccurredOn, ExpenseId ExpenseId,
        ExpenseLineId LineId);
}

/// <summary>
/// Rule-based category guess. Gathers the prior lines whose description matches the item text, groups them
/// by category, and returns the category whose most recent match is newest — frequency breaks same-date
/// ties. Null when nothing matches.
/// </summary>
internal sealed class SuggestCategoryHandler(ExpensesDbContext db)
    : IQueryHandler<SuggestCategory, Guid?>
{
    public async Task<Guid?> HandleAsync(SuggestCategory query, CancellationToken ct)
    {
        var text = query.ItemText.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var pattern = $"%{Like.Escape(text)}%";

        var matches = await db.Expenses
            .AsNoTracking()
            .SelectMany(expense => expense.Lines
                .Where(line => EF.Functions.ILike(line.Description, pattern, Like.EscapeChar))
                .Select(line => new { line.CategoryId, expense.OccurredOn, expense.Id }))
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            return null;
        }

        var best = matches
            .GroupBy(match => match.CategoryId.Value)
            .Select(group => new
            {
                CategoryId = group.Key,
                MostRecent = group.Max(match => match.OccurredOn),
                Frequency = group.Count(),
            })
            .OrderByDescending(category => category.MostRecent)
            .ThenByDescending(category => category.Frequency)
            .ThenByDescending(category => category.CategoryId)
            .First();

        return best.CategoryId;
    }
}

/// <summary>
/// Read model for the expense list. Loads matching expenses (with their lines) newest-first and resolves
/// line category names from the categories table in one extra round trip, mapping to the view in memory.
/// Currency/Total are the aggregate's derived values, read off the materialized lines. Personal scale —
/// no pagination yet.
/// </summary>
internal sealed class ListExpensesHandler(ExpensesDbContext db)
    : IQueryHandler<ListExpenses, IReadOnlyList<ExpenseRowView>>
{
    public async Task<IReadOnlyList<ExpenseRowView>> HandleAsync(ListExpenses query, CancellationToken ct)
    {
        var expenses = db.Expenses.AsNoTracking().Include(expense => expense.Lines).AsQueryable();

        if (query.From is DateOnly from)
        {
            expenses = expenses.Where(expense => expense.OccurredOn >= from);
        }

        if (query.To is DateOnly to)
        {
            expenses = expenses.Where(expense => expense.OccurredOn <= to);
        }

        if (query.CategoryFilter is Guid categoryFilter)
        {
            var category = new CategoryId(categoryFilter);
            expenses = expenses.Where(expense => expense.Lines.Any(line => line.CategoryId == category));
        }

        var rows = await expenses
            .OrderByDescending(expense => expense.OccurredOn)
            .ThenByDescending(expense => expense.Id)
            .ToListAsync(ct);

        var nameById = (await db.Categories
                .AsNoTracking()
                .Select(category => new { category.Id, category.Name })
                .ToListAsync(ct))
            .ToDictionary(category => category.Id, category => category.Name);

        return rows
            .Select(expense => new ExpenseRowView(
                expense.Id.Value,
                expense.OccurredOn,
                expense.Currency.Code,
                expense.Total.Amount,
                expense.Note,
                expense.Lines
                    .Select(line => new ExpenseLineRowView(
                        line.Description,
                        line.CategoryId.Value,
                        nameById.GetValueOrDefault(line.CategoryId, string.Empty),
                        line.Amount.Amount,
                        line.Quantity?.Value,
                        line.Quantity?.Unit.ToCode()))
                    .ToList()))
            .ToList();
    }
}

/// <summary>
/// Read model for the monthly summary (0012). Groups line amounts by the line's category and the expense's
/// currency over an inclusive date range, summing in SQL (<c>AsNoTracking</c>, no aggregate load), then
/// resolves category names from the categories table in one extra round trip. Returns one row per
/// (category × currency) — lossless and unconverted; the host folds the currency buckets into a single
/// display currency. EF for now: the grouped <c>SUM</c> translates cleanly at personal scale; Dapper waits
/// for the first read EF can't express (0012 D4).
/// </summary>
internal sealed class GetMonthlySummaryHandler(ExpensesDbContext db)
    : IQueryHandler<GetMonthlySummary, IReadOnlyList<CategorySpendView>>
{
    public async Task<IReadOnlyList<CategorySpendView>> HandleAsync(GetMonthlySummary query, CancellationToken ct)
    {
        var buckets = await db.Expenses
            .AsNoTracking()
            .Where(expense => expense.OccurredOn >= query.From && expense.OccurredOn <= query.To)
            .SelectMany(expense => expense.Lines)
            .GroupBy(line => new { line.CategoryId, line.Amount.Currency })
            .Select(group => new
            {
                group.Key.CategoryId,
                group.Key.Currency,
                Total = group.Sum(line => line.Amount.Amount),
            })
            .ToListAsync(ct);

        if (buckets.Count == 0)
        {
            return [];
        }

        var categoryIds = buckets.Select(bucket => bucket.CategoryId).Distinct().ToList();
        var nameById = (await db.Categories
                .AsNoTracking()
                .Where(category => categoryIds.Contains(category.Id))
                .Select(category => new { category.Id, category.Name })
                .ToListAsync(ct))
            .ToDictionary(category => category.Id, category => category.Name);

        return buckets
            .Select(bucket => new CategorySpendView(
                bucket.CategoryId.Value,
                nameById.GetValueOrDefault(bucket.CategoryId, string.Empty),
                bucket.Currency.Code,
                bucket.Total))
            .ToList();
    }
}

/// <summary>
/// Resolves external item text to a learned <see cref="ItemMapping"/> by <b>exact</b> match on the normalised
/// source text (0021): a hit is a previously-seen line, a miss is a new item the user resolves manually. No
/// semantic/embedding recognition — that guessed wrong (لیمو→هویج) and is dropped from the read path.
/// Embeddings are still written on confirm but no longer read here (kept dormant for Insights). EF read,
/// consistent with the other simple read models in this file.
/// </summary>
internal sealed class MatchItemHandler(ExpensesDbContext db) : IQueryHandler<MatchItem, ItemMatchView?>
{
    public async Task<ItemMatchView?> HandleAsync(MatchItem query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trimmed = query.SourceText?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        string normalized;
        try
        {
            // The exact key the write side stores, so query and stored text are comparable. A line too long to
            // ever have been stored can't match anything → treat as a miss instead of throwing.
            normalized = ItemMapping.Normalize(trimmed);
        }
        catch (DomainException)
        {
            return null;
        }

        var row = await db.ItemMappings
            .AsNoTracking()
            .Where(mapping => mapping.SourceText == normalized)
            .Select(mapping => new { mapping.CanonicalDescription, mapping.Unit, mapping.CategoryId })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : new ItemMatchView(row.CanonicalDescription, row.Unit, row.CategoryId);
    }
}

/// <summary>SQL <c>LIKE</c>/<c>ILIKE</c> escaping for user-typed text, with an explicit escape char.</summary>
file static class Like
{
    public const string EscapeChar = "\\";

    public static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
