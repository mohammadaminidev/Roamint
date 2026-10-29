using Roamint.Receipts.Domain;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Infrastructure.Outbox;

/// <summary>
/// Stable discriminator strings for the domain events the module captures — deliberately not the
/// assembly-qualified type name. The drain slice resolves these back to types once a consumer exists.
/// </summary>
internal static class OutboxEventTypes
{
    private static readonly Dictionary<Type, string> Discriminators = new()
    {
        [typeof(ReceiptUploaded)] = "receipts.receipt-uploaded",
        [typeof(ReceiptParsed)] = "receipts.receipt-parsed",
        [typeof(ReceiptParseFailed)] = "receipts.receipt-parse-failed",
        [typeof(ReceiptLinked)] = "receipts.receipt-linked",
        [typeof(OrderImported)] = "receipts.order-imported",
        [typeof(ImportedOrderLinked)] = "receipts.imported-order-linked",
        [typeof(InboxItemReceived)] = "receipts.inbox-item-received",
        [typeof(InboxItemLinked)] = "receipts.inbox-item-linked",
    };

    public static string DiscriminatorFor(IDomainEvent domainEvent)
    {
        if (Discriminators.TryGetValue(domainEvent.GetType(), out var discriminator))
        {
            return discriminator;
        }

        throw new InvalidOperationException(
            $"No outbox discriminator registered for domain event '{domainEvent.GetType().Name}'.");
    }
}
