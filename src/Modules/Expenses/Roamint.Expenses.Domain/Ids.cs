namespace Roamint.Expenses.Domain;

/// <summary>Identity of an <see cref="Expense"/> aggregate.</summary>
public readonly record struct ExpenseId(Guid Value)
{
    public static ExpenseId New(TimeProvider clock) => new(Guid.CreateVersion7(clock.GetUtcNow()));
}

/// <summary>Identity of a <see cref="Category"/> aggregate.</summary>
public readonly record struct CategoryId(Guid Value)
{
    public static CategoryId New(TimeProvider clock) => new(Guid.CreateVersion7(clock.GetUtcNow()));
}

/// <summary>Identity of an <see cref="ExpenseLine"/> entity (owned by <see cref="Expense"/>).</summary>
public readonly record struct ExpenseLineId(Guid Value)
{
    public static ExpenseLineId New(TimeProvider clock) => new(Guid.CreateVersion7(clock.GetUtcNow()));
}
