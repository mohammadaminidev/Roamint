using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Receipts;

/// <summary>The review-screen view of a receipt: its <see cref="ReceiptStatus"/> (canonical name) and the OCR
/// draft, if any. Handled by a read model in Infrastructure.</summary>
public sealed record ReceiptView(
    Guid ReceiptId,
    string Status,
    Guid? LinkedExpenseId,
    DraftView? Draft);

public sealed record DraftView(
    string? Merchant,
    DateOnly? Date,
    string? CurrencyCode,
    IReadOnlyList<DraftLineView> Lines);

public sealed record DraftLineView(
    string Description,
    decimal Amount,
    decimal? QuantityValue,
    string? QuantityUnit,
    string? SourceText);

/// <summary>Fetches a receipt's current state for the review screen; null if the id is unknown.</summary>
public sealed record GetReceipt(ReceiptId ReceiptId) : IQuery<ReceiptView?>;
