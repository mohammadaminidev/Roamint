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

internal sealed class InMemoryTagRepository : ITagRepository
{
    private readonly Dictionary<TagId, Tag> _store = [];

    public Task<Tag?> GetAsync(TagId id, CancellationToken ct) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<Tag>> GetManyAsync(IReadOnlyCollection<TagId> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Tag>>(ids.Where(_store.ContainsKey).Select(id => _store[id]).ToList());

    public Task<Tag?> FindActiveByNameAsync(string name, CategoryId? scope, CancellationToken ct) =>
        Task.FromResult(_store.Values.FirstOrDefault(tag =>
            tag.Status == TagStatus.Active
            && tag.Scope == scope
            && string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(Tag tag, CancellationToken ct)
    {
        _store[tag.Id] = tag;
        return Task.CompletedTask;
    }

    public Tag Seed(Tag tag)
    {
        _store[tag.Id] = tag;
        return tag;
    }

    public IReadOnlyCollection<Tag> All => _store.Values;
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
