namespace Roamint.Receipts.Contracts;

/// <summary>
/// The Receipts module's public order-import API (0017). Separate from <see cref="IReceiptsApi"/> so the
/// host's paste-an-order flow doesn't masquerade as a receipt upload, even though both land on the same
/// review screen via the shared <see cref="ParsedDraftDto"/>. Unrecognized payloads surface as the kernel's
/// <c>DomainException</c> (a friendly message), the same way receipt errors do.
/// </summary>
public interface IImportsApi
{
    /// <summary>Parses pasted order JSON into a draft and lands it as an imported order. A re-paste of a known
    /// order returns <see cref="ImportOrderResult.AlreadyImported"/> with the expense it already produced.</summary>
    Task<ImportOrderResult> ImportAsync(ImportOrderRequest request, CancellationToken ct = default);

    /// <summary>Links an imported order to the expense the user recorded from it. Called by the host after a
    /// successful <c>RecordExpense</c>; the link is an opaque expense id (no cross-schema FK).</summary>
    Task LinkAsync(Guid importId, Guid expenseId, CancellationToken ct = default);
}

/// <summary>The raw order JSON copied from the shop, to be parsed.</summary>
public sealed record ImportOrderRequest(string RawJson);

/// <summary>Outcome of an import: the order id, the parser that recognized it, whether it was already imported
/// (and if so the expense it produced), and the draft to review.</summary>
public sealed record ImportOrderResult(
    Guid ImportId,
    string Source,
    bool AlreadyImported,
    Guid? ExistingExpenseId,
    ParsedDraftDto Draft);
