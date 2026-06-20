namespace Roamint.Receipts.Domain;

/// <summary>Lifecycle of a <see cref="Receipt"/>. <see cref="Linked"/> and <see cref="Failed"/> are terminal.</summary>
public enum ReceiptStatus
{
    /// <summary>Image stored; OCR not yet attempted.</summary>
    Uploaded,

    /// <summary>OCR produced a <see cref="ParsedDraft"/>, awaiting review.</summary>
    Parsed,

    /// <summary>Reviewed and recorded; the resulting expense id is held opaquely.</summary>
    Linked,

    /// <summary>OCR failed; the user may still record an expense manually.</summary>
    Failed,
}
