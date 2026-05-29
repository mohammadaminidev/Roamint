using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain;

public sealed record CategoryCreated(CategoryId CategoryId, string Name) : IDomainEvent;

public sealed record CategoryRenamed(CategoryId CategoryId, string PreviousName, string NewName) : IDomainEvent;

public sealed record CategoryArchived(CategoryId CategoryId) : IDomainEvent;

public sealed record ExpenseRecorded(
    ExpenseId ExpenseId,
    CategoryId CategoryId,
    Money Amount,
    DateOnly OccurredOn) : IDomainEvent;

public sealed record ExpenseRecategorized(
    ExpenseId ExpenseId,
    CategoryId PreviousCategoryId,
    CategoryId NewCategoryId) : IDomainEvent;

public sealed record ExpenseCorrected(ExpenseId ExpenseId, Money Amount, DateOnly OccurredOn) : IDomainEvent;
