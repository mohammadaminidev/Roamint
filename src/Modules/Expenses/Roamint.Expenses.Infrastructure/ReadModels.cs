using Microsoft.EntityFrameworkCore;
using Roamint.Application.Abstractions;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;

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
                    expense.Id)))
            .ToListAsync(ct);

        return rows
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
            .Select(entry => new ItemSuggestionView(
                entry.mostRecent.Text,
                entry.mostRecent.CategoryId.Value,
                entry.mostRecent.Unit?.ToCode()))
            .ToList();
    }

    private readonly record struct Candidate(
        string Text, CategoryId CategoryId, QuantityUnit? Unit, DateOnly OccurredOn, ExpenseId ExpenseId);
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

/// <summary>SQL <c>LIKE</c>/<c>ILIKE</c> escaping for user-typed text, with an explicit escape char.</summary>
file static class Like
{
    public const string EscapeChar = "\\";

    public static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
