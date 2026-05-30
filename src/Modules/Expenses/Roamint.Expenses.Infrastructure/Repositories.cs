using Microsoft.EntityFrameworkCore;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure;

internal sealed class CategoryRepository(ExpensesDbContext db) : ICategoryRepository
{
    public Task<Category?> GetAsync(CategoryId id, CancellationToken ct) =>
        db.Categories.FirstOrDefaultAsync(category => category.Id == id, ct);

    public Task AddAsync(Category category, CancellationToken ct)
    {
        // IDs are app-assigned (Guid v7), so no async value generation — plain Add suffices.
        db.Categories.Add(category);
        return Task.CompletedTask;
    }
}

internal sealed class ExpenseRepository(ExpensesDbContext db) : IExpenseRepository
{
    // Lines is an owned collection, so EF loads it with the aggregate — no Include needed.
    public Task<Expense?> GetAsync(ExpenseId id, CancellationToken ct) =>
        db.Expenses.FirstOrDefaultAsync(expense => expense.Id == id, ct);

    public Task AddAsync(Expense expense, CancellationToken ct)
    {
        db.Expenses.Add(expense);
        return Task.CompletedTask;
    }
}

internal sealed class UnitOfWork(ExpensesDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
