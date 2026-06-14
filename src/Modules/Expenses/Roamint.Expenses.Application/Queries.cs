using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application;

/// <summary>Read projection of an active category, for selection UIs (the add-expense chips).</summary>
public sealed record CategoryView(Guid Id, string Name);

/// <summary>Lists active (non-archived) categories, name-ordered. Handled by a read model in Infrastructure.</summary>
public sealed record ListActiveCategories : IQuery<IReadOnlyList<CategoryView>>;

/// <summary>Read projection of an active tag. <c>Scope</c> is null for a global tag.</summary>
public sealed record TagView(Guid Id, string Name, Guid? Scope);

/// <summary>
/// Lists active tags, name-ordered. With <c>CategoryFilter</c> set, returns the picker set for a line of
/// that category — global tags ∪ tags scoped to it. With it null, returns every active tag (management).
/// Read model in Infrastructure.
/// </summary>
public sealed record ListTags(Guid? CategoryFilter) : IQuery<IReadOnlyList<TagView>>;

/// <summary>
/// One autocomplete suggestion for an item: the historical line text plus the category, quantity unit, and
/// tags last used with it, so picking it can fill the field, light the matching chip, pre-select the unit,
/// and pre-select its tags in one payload. <c>CategoryId</c> is null only if the source line somehow had no
/// category (the schema disallows it); <c>Unit</c> is the canonical code (<c>"g"</c>/<c>"ml"</c>/<c>"Pcs"</c>)
/// or null when the remembered line carried no quantity; <c>TagIds</c> is the remembered line's tag set.
/// </summary>
public sealed record ItemSuggestionView(string Text, Guid? CategoryId, string? Unit, IReadOnlyList<Guid> TagIds);

/// <summary>
/// Autocomplete over prior item text — distinct line descriptions matching <c>Prefix</c> (prefix matches
/// ranked before contains, then by recency), capped at <c>Limit</c>. Read model in Infrastructure.
/// </summary>
public sealed record SuggestItems(string Prefix, int Limit) : IQuery<IReadOnlyList<ItemSuggestionView>>;

/// <summary>
/// Rule-based category guess for free-typed item text: the most-recently-used category among prior lines
/// whose description matches <c>ItemText</c> (ties broken by frequency), or null when there is no signal.
/// Read model in Infrastructure.
/// </summary>
public sealed record SuggestCategory(string ItemText) : IQuery<Guid?>;

/// <summary>A line on an expense list row: its description, category (id + name), the amount paid, and the
/// optional quantity (value + canonical unit code, or null).</summary>
public sealed record ExpenseLineRowView(
    string Description,
    Guid CategoryId,
    string CategoryName,
    decimal Amount,
    decimal? QuantityValue,
    string? QuantityUnit);

/// <summary>An expense list row in its original currency — never converted in the module (FX is a host-side
/// read concern, 0010/0011). <c>Total</c>/<c>CurrencyCode</c> are derived from the lines.</summary>
public sealed record ExpenseRowView(
    Guid Id,
    DateOnly OccurredOn,
    string CurrencyCode,
    decimal Total,
    string? Note,
    IReadOnlyList<ExpenseLineRowView> Lines);

/// <summary>
/// Lists expenses newest-first for the list screen, optionally bounded by a date range and a line-level
/// category filter (an expense matches if any line is in the category). Currency is a display denomination,
/// not a filter (0010 D5a). Read model in Infrastructure.
/// </summary>
public sealed record ListExpenses(DateOnly? From, DateOnly? To, Guid? CategoryFilter)
    : IQuery<IReadOnlyList<ExpenseRowView>>;
