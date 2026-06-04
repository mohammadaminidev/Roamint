using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Application;

/// <summary>A referenced <see cref="Category"/> does not exist. An orchestration outcome, not an
/// aggregate invariant, so it lives in the application layer rather than the domain.</summary>
public sealed class CategoryNotFoundException(CategoryId id)
    : DomainException($"Category '{id.Value}' was not found.")
{
    public CategoryId CategoryId { get; } = id;
}

/// <summary>A referenced <see cref="Expense"/> does not exist.</summary>
public sealed class ExpenseNotFoundException(ExpenseId id)
    : DomainException($"Expense '{id.Value}' was not found.")
{
    public ExpenseId ExpenseId { get; } = id;
}

/// <summary>A referenced <see cref="Tag"/> does not exist.</summary>
public sealed class TagNotFoundException(TagId id)
    : DomainException($"Tag '{id.Value}' was not found.")
{
    public TagId TagId { get; } = id;
}
