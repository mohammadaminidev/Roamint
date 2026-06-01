using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application;

/// <summary>Read projection of an active category, for selection UIs (the add-expense chips).</summary>
public sealed record CategoryView(Guid Id, string Name);

/// <summary>Lists active (non-archived) categories, name-ordered. Handled by a read model in Infrastructure.</summary>
public sealed record ListActiveCategories : IQuery<IReadOnlyList<CategoryView>>;

/// <summary>
/// One autocomplete suggestion for an item: the historical line text plus the category and quantity unit
/// last used with it, so picking it can fill the field, light the matching chip, and pre-select the unit
/// in one payload. <c>CategoryId</c> is null only if the source line somehow had no category (the schema
/// disallows it); <c>Unit</c> is the canonical code (<c>"g"</c>/<c>"ml"</c>/<c>"Pcs"</c>) or null when the
/// remembered line carried no quantity.
/// </summary>
public sealed record ItemSuggestionView(string Text, Guid? CategoryId, string? Unit);

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
