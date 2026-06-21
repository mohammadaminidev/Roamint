namespace Roamint.Web.Receipts;

/// <summary>
/// Carries a parsed-and-matched receipt from the upload page to the add-expense form within the same Blazor
/// Server circuit. The host has already resolved each line against the Expenses item vocabulary (0016), so a
/// matched line arrives with its learned canonical description + category + unit, and an unmatched line
/// carries the first-time Farsi translation. Scoped to the circuit; the add page consumes and clears it on
/// load. Keeps the two pages decoupled and avoids threading the draft through the URL.
/// </summary>
public sealed class ReceiptHandoff
{
    public ReceiptSeed? Pending { get; private set; }

    public bool HasPending => Pending is not null;

    public void Set(ReceiptSeed seed) => Pending = seed;

    public void Clear() => Pending = null;
}

public sealed record ReceiptSeed(
    Guid ReceiptId,
    string? CurrencyCode,
    DateOnly? Date,
    IReadOnlyList<ReceiptLineSeed> Lines);

/// <summary>One review-form line seeded from a receipt. <see cref="SourceText"/> is the original Armenian
/// (kept so a confirmed line can be learned as a mapping). <see cref="CategoryId"/> and <see cref="Unit"/> are
/// set only when the line matched a learned mapping; otherwise <see cref="Description"/> is the Farsi
/// translation and the user assigns the category.</summary>
public sealed record ReceiptLineSeed(
    string? SourceText,
    string Description,
    decimal Amount,
    Guid? CategoryId,
    string? Unit);
