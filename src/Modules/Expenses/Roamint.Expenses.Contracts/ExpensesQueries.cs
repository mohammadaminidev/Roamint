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
}
