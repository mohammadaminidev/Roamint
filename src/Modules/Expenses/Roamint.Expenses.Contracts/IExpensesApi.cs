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

    Task CorrectExpenseAsync(Guid expenseId, CorrectExpenseRequest request, CancellationToken ct = default);
}

public sealed record CreateCategoryRequest(string Name);

/// <summary>
/// A line item on a record/correct request. The item is the categorized unit, so each line carries its
/// own <see cref="CategoryId"/> and the <see cref="Amount"/> paid. <see cref="QuantityValue"/> /
/// <see cref="QuantityUnit"/> are optional and travel together — supply both or neither; the unit is the
/// canonical code (<c>"g"</c>/<c>"ml"</c>/<c>"Pcs"</c>).
/// </summary>
public sealed record ExpenseLineDto(
    string Description,
    Guid CategoryId,
    decimal Amount,
    decimal? QuantityValue = null,
    string? QuantityUnit = null);

public sealed record RecordExpenseRequest(
    string CurrencyCode,
    DateOnly OccurredOn,
    IReadOnlyList<ExpenseLineDto> Lines,
    string? Note = null);

public sealed record CorrectExpenseRequest(
    string CurrencyCode,
    DateOnly OccurredOn,
    IReadOnlyList<ExpenseLineDto> Lines,
    string? Note = null);
