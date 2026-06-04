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

internal sealed class TagRepository(ExpensesDbContext db) : ITagRepository
{
    public Task<Tag?> GetAsync(TagId id, CancellationToken ct) =>
        db.Tags.FirstOrDefaultAsync(tag => tag.Id == id, ct);

    public async Task<IReadOnlyList<Tag>> GetManyAsync(IReadOnlyCollection<TagId> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var idList = ids.ToList();
        return await db.Tags.Where(tag => idList.Contains(tag.Id)).ToListAsync(ct);
    }

    public Task<Tag?> FindActiveByNameAsync(string name, CategoryId? scope, CancellationToken ct)
    {
        // Exact, case-insensitive match within scope (no wildcards — the name is escaped for ILIKE).
        var pattern = name
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return db.Tags.FirstOrDefaultAsync(
            tag => tag.Status == TagStatus.Active
                && tag.Scope == scope
                && EF.Functions.ILike(tag.Name, pattern, "\\"),
            ct);
    }

    public Task AddAsync(Tag tag, CancellationToken ct)
    {
        db.Tags.Add(tag);
        return Task.CompletedTask;
    }
}

internal sealed class ExpenseRepository(ExpensesDbContext db) : IExpenseRepository
{
    // Lines are a regular child entity (0008 D4b), so load them explicitly; the owned Quantity on each
    // line comes along automatically.
    public Task<Expense?> GetAsync(ExpenseId id, CancellationToken ct) =>
        db.Expenses.Include(expense => expense.Lines).FirstOrDefaultAsync(expense => expense.Id == id, ct);

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
