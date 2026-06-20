using Roamint.SharedKernel;

namespace Roamint.Receipts.Domain;

public sealed record ReceiptUploaded(ReceiptId ReceiptId, string ImageKey, DateTimeOffset UploadedOnUtc) : IDomainEvent;

public sealed record ReceiptParsed(ReceiptId ReceiptId, DateTimeOffset ParsedOnUtc) : IDomainEvent;

public sealed record ReceiptParseFailed(ReceiptId ReceiptId, string Reason) : IDomainEvent;

public sealed record ReceiptLinked(ReceiptId ReceiptId, Guid ExpenseId) : IDomainEvent;
