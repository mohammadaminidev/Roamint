using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application;

/// <summary>Read projection of an active category, for selection UIs (the add-expense chips).</summary>
public sealed record CategoryView(Guid Id, string Name);

/// <summary>Lists active (non-archived) categories, name-ordered. Handled by a read model in Infrastructure.</summary>
public sealed record ListActiveCategories : IQuery<IReadOnlyList<CategoryView>>;
