using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application;

// ---- Category ----

public sealed record CreateCategory(string Name) : ICommand<CategoryId>;

public sealed record RenameCategory(CategoryId CategoryId, string NewName) : ICommand;

public sealed record ArchiveCategory(CategoryId CategoryId) : ICommand;

// ---- Expense ----

/// <summary>Input shape for a line item on a command; the handler binds the amount to the expense's
/// currency and resolves the optional quantity (value + unit, both or neither) before handing it to
/// the domain. The item is the categorized unit, so the category lives here, not on the expense.</summary>
public sealed record ExpenseLineInput(
    string Description,
    CategoryId CategoryId,
    decimal Amount,
    decimal? QuantityValue = null,
    QuantityUnit? QuantityUnit = null);

public sealed record RecordExpense(
    string CurrencyCode,
    DateOnly OccurredOn,
    IReadOnlyList<ExpenseLineInput> Lines,
    string? Note = null) : ICommand<ExpenseId>;

public sealed record CorrectExpense(
    ExpenseId ExpenseId,
    string CurrencyCode,
    DateOnly OccurredOn,
    IReadOnlyList<ExpenseLineInput> Lines,
    string? Note = null) : ICommand;
