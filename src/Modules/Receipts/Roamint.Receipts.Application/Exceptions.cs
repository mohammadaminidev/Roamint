using Roamint.Receipts.Domain;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Application;

/// <summary>A referenced <see cref="Receipt"/> does not exist. An orchestration outcome, not an aggregate
/// invariant, so it lives in the application layer rather than the domain.</summary>
public sealed class ReceiptNotFoundException(ReceiptId id)
    : DomainException($"Receipt '{id.Value}' was not found.")
{
    public ReceiptId ReceiptId { get; } = id;
}

/// <summary>No registered parser recognized the pasted payload. An orchestration outcome surfaced to the
/// import page as a friendly message, mirroring how kernel <see cref="DomainException"/>s surface today.</summary>
public sealed class UnrecognizedImportException()
    : DomainException("Couldn't recognize this order — the pasted text isn't from a supported source.");

/// <summary>A referenced <see cref="ImportedOrder"/> does not exist. Application-layer orchestration outcome.</summary>
public sealed class ImportedOrderNotFoundException(ImportedOrderId id)
    : DomainException($"Imported order '{id.Value}' was not found.")
{
    public ImportedOrderId ImportedOrderId { get; } = id;
}

/// <summary>A referenced <see cref="InboxItem"/> does not exist. Application-layer orchestration outcome.</summary>
public sealed class InboxItemNotFoundException(InboxItemId id)
    : DomainException($"Inbox item '{id.Value}' was not found.")
{
    public InboxItemId InboxItemId { get; } = id;
}
