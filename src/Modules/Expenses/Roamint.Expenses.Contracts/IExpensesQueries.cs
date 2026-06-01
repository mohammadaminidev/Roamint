namespace Roamint.Expenses.Contracts;

/// <summary>
/// The Expenses module's public read API — the query counterpart to <see cref="IExpensesApi"/>. Returns
/// primitive DTOs; strongly-typed ids and internal projections stay inside the module.
/// </summary>
public interface IExpensesQueries
{
    Task<IReadOnlyList<CategoryListItem>> ListActiveCategoriesAsync(CancellationToken ct = default);
}

public sealed record CategoryListItem(Guid Id, string Name);
