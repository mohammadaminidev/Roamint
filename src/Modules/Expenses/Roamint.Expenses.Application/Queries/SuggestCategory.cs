using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application.Queries;

/// <summary>
/// Rule-based category guess for free-typed item text: the most-recently-used category among prior lines
/// whose description matches <c>ItemText</c> (ties broken by frequency), or null when there is no signal.
/// Read model in Infrastructure.
/// </summary>
public sealed record SuggestCategory(string ItemText) : IQuery<Guid?>;
