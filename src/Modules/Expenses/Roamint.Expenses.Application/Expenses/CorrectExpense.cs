using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Application.Expenses;

public sealed record CorrectExpense(
    ExpenseId ExpenseId,
    string CurrencyCode,
    DateOnly OccurredOn,
    IReadOnlyList<ExpenseLineInput> Lines,
    string? Note = null) : ICommand;

internal sealed class CorrectExpenseHandler(
    IExpenseRepository expenses,
    ICategoryRepository categories,
    ITagRepository tags,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CorrectExpense>
{
    public async Task HandleAsync(CorrectExpense command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var expense = await expenses.GetAsync(command.ExpenseId, ct)
            ?? throw new ExpenseNotFoundException(command.ExpenseId);

        var currency = Currency.FromCode(command.CurrencyCode);
        await ExpenseLineBinding.EnsureActiveCategoriesAsync(categories, command.Lines, ct);
        await ExpenseLineBinding.EnsureCompatibleTagsAsync(tags, command.Lines, ct);

        expense.Correct(
            command.OccurredOn,
            ExpenseLineBinding.ToDrafts(command.Lines, currency),
            clock,
            command.Note);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
