using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application.Queries;

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
