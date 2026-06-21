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

    /// <summary>Creates a tag (scope null = global; non-null = scoped to that category) and returns its id.
    /// Idempotent on the active (name, scope) pair — a collision returns the existing tag's id.</summary>
    Task<Guid> CreateTagAsync(CreateTagRequest request, CancellationToken ct = default);

    Task RenameTagAsync(Guid tagId, string newName, CancellationToken ct = default);

    Task ArchiveTagAsync(Guid tagId, CancellationToken ct = default);

    Task<Guid> RecordExpenseAsync(RecordExpenseRequest request, CancellationToken ct = default);

    Task CorrectExpenseAsync(Guid expenseId, CorrectExpenseRequest request, CancellationToken ct = default);

    /// <summary>Learns (or updates) the mapping from external item text (e.g. an Armenian receipt line) to a
    /// canonical item — the user's final description, unit, and category — with its embedding. A later
    /// confirmation of the same source wins, so a rename re-points it (0016).</summary>
    Task RecordItemMappingAsync(RecordItemMappingRequest request, CancellationToken ct = default);
}

/// <summary>A learned item mapping to record. <see cref="SourceText"/> is the external text (Armenian receipt
/// line); the rest is the canonical resolution the user confirmed.</summary>
public sealed record RecordItemMappingRequest(
    string SourceText,
    string CanonicalDescription,
    string? Unit,
    Guid CategoryId);

public sealed record CreateCategoryRequest(string Name);

/// <summary>A tag to create. <see cref="Scope"/> null = global; non-null = scoped to that category.</summary>
public sealed record CreateTagRequest(string Name, Guid? Scope);

/// <summary>
/// A line item on a record/correct request. The item is the categorized unit, so each line carries its
/// own <see cref="CategoryId"/> and the <see cref="Amount"/> paid. <see cref="QuantityValue"/> /
/// <see cref="QuantityUnit"/> are optional and travel together — supply both or neither; the unit is the
/// canonical code (<c>"g"</c>/<c>"ml"</c>/<c>"Pcs"</c>). <see cref="TagIds"/> are the 0..n tags applied to
/// the line; each must be active and scope-compatible with <see cref="CategoryId"/>.
/// </summary>
public sealed record ExpenseLineDto(
    string Description,
    Guid CategoryId,
    decimal Amount,
    decimal? QuantityValue = null,
    string? QuantityUnit = null,
    IReadOnlyList<Guid>? TagIds = null);

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
