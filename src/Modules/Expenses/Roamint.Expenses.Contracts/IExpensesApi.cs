namespace Roamint.Expenses.Contracts;

/// <summary>
/// The Expenses module's public write API. The web host depends only on this interface and the request
/// DTOs below — strongly-typed ids and the internal commands stay inside the module. Business-rule
/// violations surface as the kernel's <c>DomainException</c> (and its module subtypes).
/// </summary>
public interface IExpensesApi
{
    Task<Guid> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default);

    Task RenameCategoryAsync(Guid categoryId, string newName, CancellationToken ct = default);

    Task ArchiveCategoryAsync(Guid categoryId, CancellationToken ct = default);

    Task<Guid> RecordExpenseAsync(RecordExpenseRequest request, CancellationToken ct = default);

    Task RecategorizeExpenseAsync(Guid expenseId, Guid categoryId, CancellationToken ct = default);

    Task CorrectExpenseAsync(Guid expenseId, CorrectExpenseRequest request, CancellationToken ct = default);
}

public sealed record CreateCategoryRequest(string Name);

public sealed record ExpenseLineDto(string Description, decimal Quantity, decimal UnitPrice);

public sealed record RecordExpenseRequest(
    Guid CategoryId,
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    string? Note = null,
    IReadOnlyList<ExpenseLineDto>? Lines = null);

public sealed record CorrectExpenseRequest(
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    string? Note = null,
    IReadOnlyList<ExpenseLineDto>? Lines = null);
