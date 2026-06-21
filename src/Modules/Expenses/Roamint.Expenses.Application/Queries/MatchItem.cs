using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.Queries;

/// <summary>The resolved item for a piece of external text: the canonical Farsi description, its quantity unit
/// (canonical code or null), and category. Returned by <see cref="MatchItem"/> when a learned mapping is
/// within the similarity threshold.</summary>
public sealed record ItemMatchView(string CanonicalDescription, string? Unit, CategoryId CategoryId);

/// <summary>
/// Resolves external item text (e.g. an Armenian receipt line) to a learned canonical item by embedding
/// nearest-neighbour within the similarity threshold, or null when nothing is close enough (a new item).
/// Read model in Infrastructure: embed the text, then a pgvector search.
/// </summary>
public sealed record MatchItem(string SourceText) : IQuery<ItemMatchView?>;
