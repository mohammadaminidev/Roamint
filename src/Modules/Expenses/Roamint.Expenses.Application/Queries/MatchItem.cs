using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.Queries;

/// <summary>The resolved item for a piece of external text: the canonical Farsi description, its quantity unit
/// (canonical code or null), and category. Returned by <see cref="MatchItem"/> when a learned mapping's
/// normalised source text exactly equals the query's.</summary>
public sealed record ItemMatchView(string CanonicalDescription, string? Unit, CategoryId CategoryId);

/// <summary>
/// Resolves external item text (e.g. an Armenian receipt line) to a learned canonical item by <b>exact</b>
/// match on the normalised source text (0021), or null when it hasn't been seen before (a new item). Read
/// model in Infrastructure. Lexical, not semantic — semantic matching guessed wrong and was dropped.
/// </summary>
public sealed record MatchItem(string SourceText) : IQuery<ItemMatchView?>;
