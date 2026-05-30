using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application;

// ---- Category ----

public sealed record CreateCategory(string Name) : ICommand<CategoryId>;

public sealed record RenameCategory(CategoryId CategoryId, string NewName) : ICommand;

public sealed record ArchiveCategory(CategoryId CategoryId) : ICommand;

// ---- Expense ----

/// <summary>Input shape for a line item on a command; the handler binds the unit price to the
/// expense's currency before handing it to the domain.</summary>
public sealed record ExpenseLineInput(string Description, decimal Quantity, decimal UnitPrice);

public sealed record RecordExpense(
    CategoryId CategoryId,
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    string? Note = null,
    IReadOnlyList<ExpenseLineInput>? Lines = null) : ICommand<ExpenseId>;

public sealed record RecategorizeExpense(ExpenseId ExpenseId, CategoryId CategoryId) : ICommand;

public sealed record CorrectExpense(
    ExpenseId ExpenseId,
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    string? Note = null,
    IReadOnlyList<ExpenseLineInput>? Lines = null) : ICommand;
