using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Application;

internal sealed class RecordExpenseHandler(
    IExpenseRepository expenses,
    ICategoryRepository categories,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RecordExpense, ExpenseId>
{
    public async Task<ExpenseId> HandleAsync(RecordExpense command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var currency = Currency.FromCode(command.CurrencyCode);
        await EnsureActiveCategoryAsync(categories, command.CategoryId, ct);

        var id = ExpenseId.New(clock);
        var expense = Expense.Record(
            id,
            command.CategoryId,
            Money.Of(command.Amount, currency),
            command.OccurredOn,
            clock,
            command.Note,
            ToDrafts(command.Lines, currency));

        await expenses.AddAsync(expense, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return id;
    }

    internal static async Task EnsureActiveCategoryAsync(
        ICategoryRepository categories,
        CategoryId categoryId,
        CancellationToken ct)
    {
        var category = await categories.GetAsync(categoryId, ct)
            ?? throw new CategoryNotFoundException(categoryId);

        if (category.Status == CategoryStatus.Archived)
        {
            throw new DomainException($"Category '{category.Name}' is archived and cannot accept expenses.");
        }
    }

    internal static IEnumerable<ExpenseLineDraft>? ToDrafts(
        IReadOnlyList<ExpenseLineInput>? lines,
        Currency currency)
    {
        if (lines is null)
        {
            return null;
        }

        return lines.Select(line =>
            new ExpenseLineDraft(line.Description, line.Quantity, Money.Of(line.UnitPrice, currency)));
    }
}

internal sealed class RecategorizeExpenseHandler(
    IExpenseRepository expenses,
    ICategoryRepository categories,
    IUnitOfWork unitOfWork) : ICommandHandler<RecategorizeExpense>
{
    public async Task HandleAsync(RecategorizeExpense command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var expense = await expenses.GetAsync(command.ExpenseId, ct)
            ?? throw new ExpenseNotFoundException(command.ExpenseId);

        await RecordExpenseHandler.EnsureActiveCategoryAsync(categories, command.CategoryId, ct);

        expense.Recategorize(command.CategoryId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

internal sealed class CorrectExpenseHandler(
    IExpenseRepository expenses,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CorrectExpense>
{
    public async Task HandleAsync(CorrectExpense command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var expense = await expenses.GetAsync(command.ExpenseId, ct)
            ?? throw new ExpenseNotFoundException(command.ExpenseId);

        var currency = Currency.FromCode(command.CurrencyCode);
        expense.Correct(
            Money.Of(command.Amount, currency),
            command.OccurredOn,
            clock,
            command.Note,
            RecordExpenseHandler.ToDrafts(command.Lines, currency));

        await unitOfWork.SaveChangesAsync(ct);
    }
}
