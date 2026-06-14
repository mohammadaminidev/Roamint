using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application.Queries;

/// <summary>Read projection of an active tag. <c>Scope</c> is null for a global tag.</summary>
public sealed record TagView(Guid Id, string Name, Guid? Scope);

/// <summary>
/// Lists active tags, name-ordered. With <c>CategoryFilter</c> set, returns the picker set for a line of
/// that category — global tags ∪ tags scoped to it. With it null, returns every active tag (management).
/// Read model in Infrastructure.
/// </summary>
public sealed record ListTags(Guid? CategoryFilter) : IQuery<IReadOnlyList<TagView>>;
