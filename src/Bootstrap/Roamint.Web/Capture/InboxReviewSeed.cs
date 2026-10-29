using Roamint.Expenses.Contracts;
using Roamint.Receipts.Contracts;

namespace Roamint.Web.Capture;

/// <summary>
/// Turns a parsed inbox item into the add form's review seed (0026). The line is resolved against the learned
/// item vocabulary (exact, 0021): a match pins its canonical description, category and unit; otherwise the
/// extracted description is shown and the user picks the category. The payer is pre-selected from the sender.
/// </summary>
public static class InboxReviewSeed
{
    /// <summary>The message's expense day: the extracted one, else the UTC day it was sent (the extractor already
    /// anchors to the Yerevan day, so the fallback is only for a draft without one).</summary>
    public static DateOnly DateOf(ParsedDraftDto draft, DateTimeOffset sentOnUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.Date ?? DateOnly.FromDateTime(sentOnUtc.UtcDateTime);
    }

    /// <summary>Null when the item has no single-line draft to review.</summary>
    public static async Task<CaptureSeed?> BuildAsync(
        InboxItemDto item, string? paidBy, IExpensesQueries queries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(queries);

        if (item.Draft is not { Lines.Count: 1 } draft)
        {
            return null;
        }

        var line = draft.Lines[0];
        var match = string.IsNullOrWhiteSpace(line.SourceText) ? null : await queries.MatchItemAsync(line.SourceText, ct);
        return new CaptureSeed(
            CaptureSource.Inbox,
            item.ItemId,
            draft.CurrencyCode,
            DateOf(draft, item.SentOnUtc),
            [new CaptureLineSeed(line.SourceText, match?.CanonicalDescription ?? line.Description, line.Amount, match?.CategoryId, match?.Unit)],
            paidBy);
    }
}
