using Roamint.Application.Abstractions;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Contracts;

/// <summary>
/// Translates primitive request DTOs into the module's strongly-typed commands and dispatches them. This
/// is where a primitive <see cref="Guid"/>/currency-code crosses into the module's internal vocabulary.
/// </summary>
internal sealed class ExpensesApi(ICommandDispatcher commands) : IExpensesApi
{
    public async Task<Guid> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = await commands.SendAsync(new CreateCategory(request.Name), ct);
        return id.Value;
    }

    public Task RenameCategoryAsync(Guid categoryId, string newName, CancellationToken ct = default) =>
        commands.SendAsync(new RenameCategory(new CategoryId(categoryId), newName), ct);

    public Task ArchiveCategoryAsync(Guid categoryId, CancellationToken ct = default) =>
        commands.SendAsync(new ArchiveCategory(new CategoryId(categoryId)), ct);

    public async Task<Guid> RecordExpenseAsync(RecordExpenseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = await commands.SendAsync(
            new RecordExpense(
                request.CurrencyCode,
                request.OccurredOn,
                ToLines(request.Lines),
                request.Note),
            ct);
        return id.Value;
    }

    public Task CorrectExpenseAsync(Guid expenseId, CorrectExpenseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return commands.SendAsync(
            new CorrectExpense(
                new ExpenseId(expenseId),
                request.CurrencyCode,
                request.OccurredOn,
                ToLines(request.Lines),
                request.Note),
            ct);
    }

    private static List<ExpenseLineInput> ToLines(IReadOnlyList<ExpenseLineDto> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(line => new ExpenseLineInput(
            line.Description,
            new CategoryId(line.CategoryId),
            line.Amount,
            line.QuantityValue,
            line.QuantityUnit is null ? null : QuantityUnits.FromCode(line.QuantityUnit))).ToList();
    }
}
