using Roamint.Application.Abstractions;
using Roamint.Expenses.Application;

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

    public async Task<IReadOnlyList<ItemSuggestion>> SuggestItemsAsync(string prefix, int limit, CancellationToken ct = default)
    {
        var suggestions = await queries.QueryAsync(new SuggestItems(prefix, limit), ct);
        return suggestions
            .Select(suggestion => new ItemSuggestion(suggestion.Text, suggestion.CategoryId, suggestion.Unit))
            .ToList();
    }

    public Task<Guid?> SuggestCategoryAsync(string itemText, CancellationToken ct = default) =>
        queries.QueryAsync(new SuggestCategory(itemText), ct);
}
