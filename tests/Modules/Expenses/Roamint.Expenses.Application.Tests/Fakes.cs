using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.Tests;

internal sealed class InMemoryCategoryRepository : ICategoryRepository
{
    private readonly Dictionary<CategoryId, Category> _store = [];

    public Task<Category?> GetAsync(CategoryId id, CancellationToken ct) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task AddAsync(Category category, CancellationToken ct)
    {
        _store[category.Id] = category;
        return Task.CompletedTask;
    }

    public Category Seed(Category category)
    {
        _store[category.Id] = category;
        return category;
    }

    public IReadOnlyCollection<Category> All => _store.Values;
}

internal sealed class InMemoryExpenseRepository : IExpenseRepository
{
    private readonly Dictionary<ExpenseId, Expense> _store = [];

    public Task<Expense?> GetAsync(ExpenseId id, CancellationToken ct) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task AddAsync(Expense expense, CancellationToken ct)
    {
        _store[expense.Id] = expense;
        return Task.CompletedTask;
    }

    public Expense Seed(Expense expense)
    {
        _store[expense.Id] = expense;
        return expense;
    }

    public IReadOnlyCollection<Expense> All => _store.Values;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
