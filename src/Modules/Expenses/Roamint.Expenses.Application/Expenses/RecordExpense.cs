using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Application.Expenses;

/// <summary>Input shape for a line item on a command; the handler binds the amount to the expense's
/// currency and resolves the optional quantity (value + unit, both or neither) before handing it to
/// the domain. The item is the categorized unit, so the category lives here, not on the expense.
/// <paramref name="TagIds"/> are the 0..n tags applied to the line; the handler validates each is active
/// and scope-compatible with <paramref name="CategoryId"/>.</summary>
public sealed record ExpenseLineInput(
    string Description,
    CategoryId CategoryId,
    decimal Amount,
    decimal? QuantityValue = null,
    QuantityUnit? QuantityUnit = null,
    IReadOnlyList<TagId>? TagIds = null);

public sealed record RecordExpense(
    string CurrencyCode,
    DateOnly OccurredOn,
    IReadOnlyList<ExpenseLineInput> Lines,
    string? Note = null,
    string? PaidBy = null) : ICommand<ExpenseId>;

internal sealed class RecordExpenseHandler(
    IExpenseRepository expenses,
    ICategoryRepository categories,
    ITagRepository tags,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RecordExpense, ExpenseId>
{
    public async Task<ExpenseId> HandleAsync(RecordExpense command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var currency = Currency.FromCode(command.CurrencyCode);
        await ExpenseLineBinding.EnsureActiveCategoriesAsync(categories, command.Lines, ct);
        await ExpenseLineBinding.EnsureCompatibleTagsAsync(tags, command.Lines, ct);

        var id = ExpenseId.New(clock);
        var expense = Expense.Record(
            id,
            command.OccurredOn,
            ExpenseLineBinding.ToDrafts(command.Lines, currency),
            clock,
            command.Note,
            command.PaidBy);

        await expenses.AddAsync(expense, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return id;
    }
}
