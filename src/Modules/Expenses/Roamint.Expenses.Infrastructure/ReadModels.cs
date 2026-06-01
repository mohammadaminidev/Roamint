using Microsoft.EntityFrameworkCore;
using Roamint.Application.Abstractions;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// Read model for the active-category list. Bypasses the aggregate and projects straight off the
/// <c>expenses.categories</c> table with <c>AsNoTracking</c> — EF for now; Dapper arrives with the
/// analytics screens.
/// </summary>
internal sealed class ListActiveCategoriesHandler(ExpensesDbContext db)
    : IQueryHandler<ListActiveCategories, IReadOnlyList<CategoryView>>
{
    public async Task<IReadOnlyList<CategoryView>> HandleAsync(ListActiveCategories query, CancellationToken ct)
    {
        // Project Id + Name (Id read back through its value converter), then map to the view client-side
        // so we don't ask the provider to translate CategoryId.Value into SQL.
        var rows = await db.Categories
            .AsNoTracking()
            .Where(category => category.Status == CategoryStatus.Active)
            .OrderBy(category => category.Name)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(ct);

        return rows.Select(row => new CategoryView(row.Id.Value, row.Name)).ToList();
    }
}
