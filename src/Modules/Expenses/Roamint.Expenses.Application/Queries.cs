using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application;

/// <summary>Read projection of an active category, for selection UIs (the add-expense chips).</summary>
public sealed record CategoryView(Guid Id, string Name);

/// <summary>Lists active (non-archived) categories, name-ordered. Handled by a read model in Infrastructure.</summary>
public sealed record ListActiveCategories : IQuery<IReadOnlyList<CategoryView>>;

/// <summary>
/// One autocomplete suggestion for an item: the historical text plus the category last used with it
/// (so picking it can light the matching chip in the same payload). <c>CategoryId</c> is null only if
/// the source row somehow had no category, which the schema disallows — kept nullable for the DTO twin.
/// </summary>
public sealed record ItemSuggestionView(string Text, Guid? CategoryId);

/// <summary>
/// Autocomplete over prior item text — distinct notes and line descriptions matching <c>Prefix</c>
/// (prefix matches ranked before contains, then by recency), capped at <c>Limit</c>. Read model in
/// Infrastructure.
/// </summary>
public sealed record SuggestItems(string Prefix, int Limit) : IQuery<IReadOnlyList<ItemSuggestionView>>;

/// <summary>
/// Rule-based category guess for free-typed item text: the most-recently-used category among prior
/// expenses whose note or any line description matches <c>ItemText</c> (ties broken by frequency), or
/// null when there is no signal. Read model in Infrastructure.
/// </summary>
public sealed record SuggestCategory(string ItemText) : IQuery<Guid?>;
