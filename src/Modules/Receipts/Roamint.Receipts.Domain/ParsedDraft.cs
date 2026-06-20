namespace Roamint.Receipts.Domain;

/// <summary>
/// What OCR extracted from a receipt image, pending human review. Every field is best-effort and nullable —
/// the model fills what it can read; the user corrects it and assigns categories at review. There is
/// deliberately no category here: OCR cannot know the user's category vocabulary.
/// </summary>
public sealed record ParsedDraft(
    string? Merchant,
    DateOnly? Date,
    string? CurrencyCode,
    IReadOnlyList<ParsedLine> Lines)
{
    /// <summary>The fallback when OCR yields nothing (e.g. the stub provider, or an unreadable image) —
    /// the review screen opens blank and the user enters lines by hand.</summary>
    public static ParsedDraft Empty { get; } = new(null, null, null, []);
}

/// <summary>
/// One extracted line item. <see cref="QuantityValue"/> and <see cref="QuantityUnit"/> travel together
/// (both or neither). The unit is a raw string code, not the Expenses domain's <c>QuantityUnit</c> —
/// Receipts must not depend on Expenses; the web host maps the code when binding the review form.
/// </summary>
public sealed record ParsedLine(
    string Description,
    decimal Amount,
    decimal? QuantityValue = null,
    string? QuantityUnit = null);
