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
                new CategoryId(request.CategoryId),
                request.Amount,
                request.CurrencyCode,
                request.OccurredOn,
                request.Note,
                ToLines(request.Lines)),
            ct);
        return id.Value;
    }

    public Task RecategorizeExpenseAsync(Guid expenseId, Guid categoryId, CancellationToken ct = default) =>
        commands.SendAsync(new RecategorizeExpense(new ExpenseId(expenseId), new CategoryId(categoryId)), ct);

    public Task CorrectExpenseAsync(Guid expenseId, CorrectExpenseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return commands.SendAsync(
            new CorrectExpense(
                new ExpenseId(expenseId),
                request.Amount,
                request.CurrencyCode,
                request.OccurredOn,
                request.Note,
                ToLines(request.Lines)),
            ct);
    }

    private static List<ExpenseLineInput>? ToLines(IReadOnlyList<ExpenseLineDto>? lines) =>
        lines?.Select(line => new ExpenseLineInput(line.Description, line.Quantity, line.UnitPrice)).ToList();
}
