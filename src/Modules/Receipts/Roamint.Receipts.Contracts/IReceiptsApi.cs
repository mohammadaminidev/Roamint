namespace Roamint.Receipts.Contracts;

/// <summary>
/// The Receipts module's public write API. The web host depends only on this interface and the DTOs below —
/// strongly-typed ids and internal commands stay inside the module. Business-rule violations surface as the
/// kernel's <c>DomainException</c> (and its module subtypes).
/// </summary>
public interface IReceiptsApi
{
    /// <summary>Stores the image, runs OCR synchronously, and returns the new receipt's id plus the parsed
    /// draft (if any) for the review screen.</summary>
    Task<UploadReceiptResult> UploadAsync(UploadReceiptRequest request, CancellationToken ct = default);

    /// <summary>Links a parsed receipt to the expense the user recorded from it. Called by the host after a
    /// successful <c>RecordExpense</c>; the link is an opaque expense id (no cross-schema FK).</summary>
    Task LinkAsync(Guid receiptId, Guid expenseId, CancellationToken ct = default);
}

/// <summary>A receipt image to upload. <see cref="ContentType"/> is the MIME type (e.g. <c>image/jpeg</c>).</summary>
public sealed record UploadReceiptRequest(byte[] Image, string ContentType);

/// <summary>Outcome of an upload: the new receipt id, whether OCR produced a draft, and that draft.</summary>
public sealed record UploadReceiptResult(Guid ReceiptId, bool Parsed, ParsedDraftDto? Draft);

/// <summary>The OCR draft surfaced to the host. All fields are best-effort; categories are assigned at review,
/// not here.</summary>
public sealed record ParsedDraftDto(
    string? Merchant,
    DateOnly? Date,
    string? CurrencyCode,
    IReadOnlyList<ParsedLineDto> Lines);

public sealed record ParsedLineDto(
    string Description,
    decimal Amount,
    decimal? QuantityValue,
    string? QuantityUnit);
