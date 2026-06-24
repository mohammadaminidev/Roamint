namespace Roamint.Receipts.Contracts;

/// <summary>Read access to receipts for the web host.</summary>
public interface IReceiptsQueries
{
    /// <summary>Fetches a receipt's current state for the review screen; null if the id is unknown.</summary>
    Task<ReceiptDto?> GetAsync(Guid receiptId, CancellationToken ct = default);

    /// <summary>Ids of receipts still uploaded-but-not-parsed, for the host's startup re-enqueue sweep (0019).</summary>
    Task<IReadOnlyList<Guid>> ListPendingAsync(CancellationToken ct = default);
}

/// <summary>A receipt's current state: <see cref="Status"/> is the canonical name
/// (<c>Uploaded</c>/<c>Parsed</c>/<c>Linked</c>/<c>Failed</c>); <see cref="Draft"/> is present once parsed.</summary>
public sealed record ReceiptDto(
    Guid ReceiptId,
    string Status,
    Guid? LinkedExpenseId,
    ParsedDraftDto? Draft);
