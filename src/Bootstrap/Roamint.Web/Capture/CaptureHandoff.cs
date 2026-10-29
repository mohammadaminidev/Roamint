namespace Roamint.Web.Capture;

/// <summary>Which external source a <see cref="CaptureSeed"/> came from — selects the linker the add-expense
/// form calls on confirm (a receipt, an imported order, or a Telegram inbox item, 0024).</summary>
public enum CaptureSource
{
    Receipt,
    ImportedOrder,
    Inbox,
}

/// <summary>
/// Carries a parsed-and-matched capture (a scanned receipt, 0015, or a pasted order, 0017) from the source
/// page to the add-expense form within the same Blazor Server circuit. The host has already resolved each
/// line against the Expenses item vocabulary (0016), so a matched line arrives with its learned canonical
/// description + category + unit, and an unmatched line carries the first-time translation. Scoped to the
/// circuit; the add page consumes and clears it on load. Keeps the pages decoupled and avoids threading the
/// draft through the URL.
/// </summary>
public sealed class CaptureHandoff
{
    public CaptureSeed? Pending { get; private set; }

    public bool HasPending => Pending is not null;

    public void Set(CaptureSeed seed) => Pending = seed;

    public void Clear() => Pending = null;
}

/// <summary>A source-neutral capture to review. <see cref="Source"/> + <see cref="SourceId"/> are the link
/// target the add form uses on confirm. <see cref="PaidBy"/> pre-selects the payer (a Telegram sender, 0024).</summary>
public sealed record CaptureSeed(
    CaptureSource Source,
    Guid SourceId,
    string? CurrencyCode,
    DateOnly? Date,
    IReadOnlyList<CaptureLineSeed> Lines,
    string? PaidBy = null);

/// <summary>One review-form line seeded from a capture. <see cref="SourceText"/> is the original item text
/// (kept so a confirmed line can be learned as a mapping). <see cref="CategoryId"/> and <see cref="Unit"/> are
/// set only when the line matched a learned mapping; otherwise <see cref="Description"/> is the first-time
/// translation and the user assigns the category.</summary>
public sealed record CaptureLineSeed(
    string? SourceText,
    string Description,
    decimal Amount,
    Guid? CategoryId,
    string? Unit);
