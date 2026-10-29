using System.Diagnostics.CodeAnalysis;
using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Inbox;

/// <summary>What reading an inbox item produced: <see cref="InboxItemStatus.Parsed"/> with its one-line draft,
/// <see cref="InboxItemStatus.Ignored"/> (not an expense), or <see cref="InboxItemStatus.Failed"/>.</summary>
public sealed record ParseInboxItemOutcome(InboxItemStatus Status, ParsedDraft? Draft);

/// <summary>
/// Reads a received (or previously failed) inbox item with the <see cref="IInboxExtractor"/> and turns the result
/// into a one-line draft (0024). The extraction is validated, not trusted: a non-positive amount becomes 0 and an
/// implausible currency becomes null, which the host routes to review. The date is anchored to the message's own
/// local day — "tonight" in a message sent Saturday and drained Monday is Saturday — and an extracted date after
/// that day is discarded. A thrown extractor fails the item (re-parseable), not the request.
/// </summary>
public sealed record ParseInboxItem(InboxItemId ItemId) : ICommand<ParseInboxItemOutcome>;

internal sealed class ParseInboxItemHandler(
    IInboxItemRepository items,
    IInboxExtractor extractor,
    IUnitOfWork unitOfWork) : ICommandHandler<ParseInboxItem, ParseInboxItemOutcome>
{
    private const int MaxDescriptionLength = 200;

    // The household lives in Armenia (UTC+4, no DST); message days are Yerevan days.
    private static readonly TimeZoneInfo Yerevan = TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan");

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Extraction is a best-effort external call; any failure degrades to a Failed item that "
            + "the next startup sweep retries, rather than surfacing as an error in the host's worker.")]
    public async Task<ParseInboxItemOutcome> HandleAsync(ParseInboxItem command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.ItemId, ct)
            ?? throw new InboxItemNotFoundException(command.ItemId);

        var sentOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.SentOnUtc, Yerevan).DateTime);

        InboxExtraction extraction;
        try
        {
            extraction = await extractor.ExtractAsync(item.Text, sentOn, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            item.MarkFailed();
            await unitOfWork.SaveChangesAsync(ct);
            return new ParseInboxItemOutcome(InboxItemStatus.Failed, null);
        }

        if (!extraction.IsExpense)
        {
            item.Ignore();
            await unitOfWork.SaveChangesAsync(ct);
            return new ParseInboxItemOutcome(InboxItemStatus.Ignored, null);
        }

        var draft = ToDraft(extraction, item.Text, sentOn);
        item.Parse(draft);
        await unitOfWork.SaveChangesAsync(ct);
        return new ParseInboxItemOutcome(InboxItemStatus.Parsed, draft);
    }

    private static ParsedDraft ToDraft(InboxExtraction extraction, string text, DateOnly sentOn)
    {
        var amount = extraction.Amount is > 0m ? extraction.Amount.Value : 0m;

        var currency = extraction.CurrencyCode?.Trim().ToUpperInvariant();
        if (currency is not { Length: >= 3 and <= 8 })
        {
            currency = null;
        }

        var date = extraction.Date is DateOnly extracted && extracted <= sentOn ? extracted : sentOn;

        var description = string.IsNullOrWhiteSpace(extraction.Description)
            ? text.Trim()
            : extraction.Description.Trim();
        if (description.Length > MaxDescriptionLength)
        {
            description = description[..MaxDescriptionLength];
        }

        // SourceText = the extracted description: the key the host matches against learned items, so "شام"
        // confirmed once resolves automatically next time (0021).
        return new ParsedDraft(null, date, currency, [new ParsedLine(description, amount, SourceText: description)]);
    }
}
