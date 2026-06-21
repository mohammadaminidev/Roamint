using Roamint.Application.Abstractions;
using Roamint.Expenses.Application.Queries;

namespace Roamint.Expenses.Contracts;

/// <summary>Dispatches read queries and maps internal projections to primitive DTOs — the read-side twin
/// of <see cref="ExpensesApi"/>.</summary>
internal sealed class ExpensesQueries(IQueryDispatcher queries) : IExpensesQueries
{
    public async Task<IReadOnlyList<CategoryListItem>> ListActiveCategoriesAsync(CancellationToken ct = default)
    {
        var categories = await queries.QueryAsync(new ListActiveCategories(), ct);
        return categories.Select(category => new CategoryListItem(category.Id, category.Name)).ToList();
    }

    public async Task<IReadOnlyList<TagListItem>> ListTagsAsync(Guid? categoryFilter = null, CancellationToken ct = default)
    {
        var tags = await queries.QueryAsync(new ListTags(categoryFilter), ct);
        return tags.Select(tag => new TagListItem(tag.Id, tag.Name, tag.Scope)).ToList();
    }

    public async Task<IReadOnlyList<ItemSuggestion>> SuggestItemsAsync(string prefix, int limit, CancellationToken ct = default)
    {
        var suggestions = await queries.QueryAsync(new SuggestItems(prefix, limit), ct);
        return suggestions
            .Select(suggestion => new ItemSuggestion(suggestion.Text, suggestion.CategoryId, suggestion.Unit, suggestion.TagIds))
            .ToList();
    }

    public Task<Guid?> SuggestCategoryAsync(string itemText, CancellationToken ct = default) =>
        queries.QueryAsync(new SuggestCategory(itemText), ct);

    public async Task<ItemMatchDto?> MatchItemAsync(string sourceText, CancellationToken ct = default)
    {
        var match = await queries.QueryAsync(new MatchItem(sourceText), ct);
        return match is null ? null : new ItemMatchDto(match.CanonicalDescription, match.Unit, match.CategoryId.Value);
    }

    public async Task<IReadOnlyList<ExpenseListItem>> ListExpensesAsync(
        DateOnly? fromDate = null, DateOnly? toDate = null, Guid? categoryFilter = null, CancellationToken ct = default)
    {
        var rows = await queries.QueryAsync(new ListExpenses(fromDate, toDate, categoryFilter), ct);
        return rows
            .Select(row => new ExpenseListItem(
                row.Id,
                row.OccurredOn,
                row.CurrencyCode,
                row.Total,
                row.Note,
                row.Lines
                    .Select(line => new ExpenseLineItem(
                        line.Description,
                        line.CategoryId,
                        line.CategoryName,
                        line.Amount,
                        line.QuantityValue,
                        line.QuantityUnit))
                    .ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<CategorySpendItem>> GetMonthlySummaryAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var buckets = await queries.QueryAsync(new GetMonthlySummary(fromDate, toDate), ct);
        return buckets
            .Select(bucket => new CategorySpendItem(
                bucket.CategoryId, bucket.CategoryName, bucket.CurrencyCode, bucket.Total))
            .ToList();
    }
}
