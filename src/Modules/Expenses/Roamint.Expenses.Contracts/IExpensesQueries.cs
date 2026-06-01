namespace Roamint.Expenses.Contracts;

/// <summary>
/// The Expenses module's public read API — the query counterpart to <see cref="IExpensesApi"/>. Returns
/// primitive DTOs; strongly-typed ids and internal projections stay inside the module.
/// </summary>
public interface IExpensesQueries
{
    Task<IReadOnlyList<CategoryListItem>> ListActiveCategoriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Autocomplete over prior item text (notes + line descriptions) matching <paramref name="prefix"/>,
    /// each carrying the category last used with it so a pick can light the chip without a second call.
    /// </summary>
    Task<IReadOnlyList<ItemSuggestion>> SuggestItemsAsync(string prefix, int limit, CancellationToken ct = default);

    /// <summary>
    /// Rule-based category guess for free-typed item text, or null when history gives no signal. Use this
    /// on free-text commit (note/line blur); <see cref="SuggestItemsAsync"/> already carries a category
    /// when the user picks from the dropdown.
    /// </summary>
    Task<Guid?> SuggestCategoryAsync(string itemText, CancellationToken ct = default);
}

public sealed record CategoryListItem(Guid Id, string Name);

/// <summary>An autocomplete suggestion: the historical item text and the category last used with it.</summary>
public sealed record ItemSuggestion(string Text, Guid? CategoryId);
