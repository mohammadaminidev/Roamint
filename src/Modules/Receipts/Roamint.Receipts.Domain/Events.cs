using Roamint.SharedKernel;

namespace Roamint.Receipts.Domain;

public sealed record ReceiptUploaded(ReceiptId ReceiptId, string ImageKey, DateTimeOffset UploadedOnUtc) : IDomainEvent;

public sealed record ReceiptParsed(ReceiptId ReceiptId, DateTimeOffset ParsedOnUtc) : IDomainEvent;

public sealed record ReceiptParseFailed(ReceiptId ReceiptId, string Reason) : IDomainEvent;

public sealed record ReceiptLinked(ReceiptId ReceiptId, Guid ExpenseId) : IDomainEvent;

public sealed record OrderImported(
    ImportedOrderId OrderId, string Source, string ExternalOrderId, DateTimeOffset ImportedOnUtc) : IDomainEvent;

public sealed record ImportedOrderLinked(ImportedOrderId OrderId, Guid ExpenseId) : IDomainEvent;

public sealed record InboxItemReceived(
    InboxItemId ItemId, string Source, string ExternalId, DateTimeOffset ReceivedOnUtc) : IDomainEvent;

public sealed record InboxItemLinked(InboxItemId ItemId, Guid ExpenseId) : IDomainEvent;
