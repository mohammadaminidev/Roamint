namespace Roamint.Receipts.Domain;

/// <summary>Lifecycle of an <see cref="InboxItem"/> (0024). <see cref="Linked"/> and <see cref="Ignored"/> are
/// terminal; <see cref="Failed"/> can be re-parsed (the extractor call failed, not the message).</summary>
public enum InboxItemStatus
{
    /// <summary>Drained from the relay, not yet read.</summary>
    Received,

    /// <summary>Read as an expense; the one-line draft awaits auto-recording or review.</summary>
    Parsed,

    /// <summary>Not an expense (chatter) or from an unknown sender.</summary>
    Ignored,

    /// <summary>The extractor call failed; retried on the next startup sweep.</summary>
    Failed,

    /// <summary>Recorded; the resulting expense id is held opaquely.</summary>
    Linked,
}
