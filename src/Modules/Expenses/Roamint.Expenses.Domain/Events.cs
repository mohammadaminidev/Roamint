using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain;

public sealed record CategoryCreated(CategoryId CategoryId, string Name) : IDomainEvent;

public sealed record CategoryRenamed(CategoryId CategoryId, string PreviousName, string NewName) : IDomainEvent;

public sealed record CategoryArchived(CategoryId CategoryId) : IDomainEvent;

public sealed record TagCreated(TagId TagId, string Name, CategoryId? Scope) : IDomainEvent;

public sealed record TagRenamed(TagId TagId, string PreviousName, string NewName) : IDomainEvent;

public sealed record TagArchived(TagId TagId) : IDomainEvent;

public sealed record ExpenseRecorded(
    ExpenseId ExpenseId,
    Money Total,
    DateOnly OccurredOn) : IDomainEvent;

public sealed record ExpenseCorrected(ExpenseId ExpenseId, Money Total, DateOnly OccurredOn) : IDomainEvent;
