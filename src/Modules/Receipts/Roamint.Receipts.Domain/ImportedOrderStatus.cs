namespace Roamint.Receipts.Domain;

/// <summary>Lifecycle of an <see cref="ImportedOrder"/>. <see cref="Linked"/> is terminal. There is no
/// failure state: parsing is synchronous and total — a pasted payload either matches a parser (an order
/// exists) or is rejected before any aggregate is created.</summary>
public enum ImportedOrderStatus
{
    /// <summary>The order was parsed into a <see cref="ParsedDraft"/>, awaiting review.</summary>
    Imported,

    /// <summary>Reviewed and recorded; the resulting expense id is held opaquely.</summary>
    Linked,
}
