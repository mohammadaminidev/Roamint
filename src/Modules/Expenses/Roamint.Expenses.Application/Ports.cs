using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application;

/// <summary>Write-side access to the <see cref="Category"/> aggregate.</summary>
public interface ICategoryRepository
{
    Task<Category?> GetAsync(CategoryId id, CancellationToken ct);

    Task AddAsync(Category category, CancellationToken ct);
}

/// <summary>Write-side access to the <see cref="Expense"/> aggregate.</summary>
public interface IExpenseRepository
{
    Task<Expense?> GetAsync(ExpenseId id, CancellationToken ct);

    Task AddAsync(Expense expense, CancellationToken ct);
}

/// <summary>Commits the work of a single use case. The transactional outbox will hook here, persisting
/// buffered domain events alongside the aggregate in one transaction.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
