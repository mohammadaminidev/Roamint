namespace Roamint.Expenses.Contracts;

/// <summary>
/// The Expenses module's public read API — the query counterpart to <see cref="IExpensesApi"/>. Returns
/// primitive DTOs; strongly-typed ids and internal projections stay inside the module.
/// </summary>
public interface IExpensesQueries
{
    Task<IReadOnlyList<CategoryListItem>> ListActiveCategoriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Active tags for selection. With <paramref name="categoryFilter"/> set, returns the picker set for a
    /// line of that category — global tags ∪ tags scoped to it. With it null, returns every active tag.
    /// </summary>
    Task<IReadOnlyList<TagListItem>> ListTagsAsync(Guid? categoryFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Autocomplete over prior item text (line descriptions) matching <paramref name="prefix"/>, each
    /// carrying the category and quantity unit last used with it so a pick can fill the field, light the
    /// chip, and pre-select the unit without a second call.
    /// </summary>
    Task<IReadOnlyList<ItemSuggestion>> SuggestItemsAsync(string prefix, int limit, CancellationToken ct = default);

    /// <summary>
    /// Rule-based category guess for free-typed item text, or null when history gives no signal. Use this
    /// on free-text commit (note/line blur); <see cref="SuggestItemsAsync"/> already carries a category
    /// when the user picks from the dropdown.
    /// </summary>
    Task<Guid?> SuggestCategoryAsync(string itemText, CancellationToken ct = default);

    /// <summary>
    /// Resolves external item text (e.g. an Armenian receipt line) to a learned canonical item by embedding
    /// similarity, or null when nothing is close enough (a new item). Used by the receipt flow to show the
    /// remembered mapping instead of re-translating (0016).
    /// </summary>
    Task<ItemMatchDto?> MatchItemAsync(string sourceText, CancellationToken ct = default);

    /// <summary>
    /// Expenses newest-first for the list screen, in their original currency (conversion is composed by the
    /// host). Optionally bounded by a date range; <paramref name="categoryFilter"/> matches an expense when
    /// any line is in that category. Currency is a display denomination, not a filter.
    /// </summary>
    Task<IReadOnlyList<ExpenseListItem>> ListExpensesAsync(
        DateOnly? fromDate = null, DateOnly? toDate = null, Guid? categoryFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Per-(category, currency) spend sums over an inclusive date range, for the host to convert and
    /// aggregate into a display-currency monthly summary. Category is line-level; currency is the expense's
    /// original currency. The breakdown is raw and lossless — the host folds the currency buckets per
    /// category, since latest-rate conversion is a host-side read concern (0012 D2).
    /// </summary>
    Task<IReadOnlyList<CategorySpendItem>> GetMonthlySummaryAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}

public sealed record CategoryListItem(Guid Id, string Name);

/// <summary>A learned item resolution: the canonical description, quantity unit (canonical code or null), and
/// category to apply to a matched receipt line.</summary>
public sealed record ItemMatchDto(string CanonicalDescription, string? Unit, Guid CategoryId);

/// <summary>A selectable tag. <see cref="Scope"/> is null for a global tag, else the category it's scoped to.</summary>
public sealed record TagListItem(Guid Id, string Name, Guid? Scope);

/// <summary>An autocomplete suggestion: the historical item text plus the category, quantity unit (canonical
/// code, or null), and the tags last used with it.</summary>
public sealed record ItemSuggestion(string Text, Guid? CategoryId, string? Unit, IReadOnlyList<Guid> TagIds);

/// <summary>A line on an expense list row: description, category (id + name), amount paid, and optional
/// quantity (value + canonical unit code, or null).</summary>
public sealed record ExpenseLineItem(
    string Description, Guid CategoryId, string CategoryName, decimal Amount, decimal? QuantityValue, string? QuantityUnit);

/// <summary>An expense list row in its original currency. <see cref="Total"/>/<see cref="CurrencyCode"/> are
/// derived from the lines; the host converts for display.</summary>
public sealed record ExpenseListItem(
    Guid Id, DateOnly OccurredOn, string CurrencyCode, decimal Total, string? Note, IReadOnlyList<ExpenseLineItem> Lines);

/// <summary>One bucket of spend: the total of all line amounts in a single (category × currency) pair over a
/// date range. Unconverted — the host folds the buckets into a display-currency total per category.</summary>
public sealed record CategorySpendItem(Guid CategoryId, string CategoryName, string CurrencyCode, decimal Total);
