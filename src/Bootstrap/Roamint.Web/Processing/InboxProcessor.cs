using System.Globalization;
using Roamint.Expenses.Contracts;
using Roamint.Receipts.Contracts;
using Roamint.SharedKernel;
using Roamint.Web.Capture;
using Roamint.Web.Services;

namespace Roamint.Web.Processing;

/// <summary>
/// Settles one inbox item (0024) — host orchestration across Receipts and Expenses, like the receipt worker:
/// <list type="number">
/// <item>An unknown sender is ignored before any extractor call.</item>
/// <item>A received (or failed) item is read into a one-line draft; chatter is ignored, a failed read shows 🤷.</item>
/// <item>The line is resolved against the learned item vocabulary (exact, 0021). It auto-records only when nothing
/// is guessed: a learned match (so the category is known), a positive amount, a supported currency, and no
/// same-day twin (0022). Then the item is linked and the bot reacts 👍 and replies with what it understood.</item>
/// <item>Anything else stays parsed for review on the inbox page (0026) and the bot reacts ✍; confirming it on the
/// form records, links and flips the reaction.</item>
/// </list>
/// Re-running on an already-parsed item re-evaluates it, so a startup sweep can auto-record items whose description
/// was learned since. Feedback is best-effort: a Telegram failure never undoes a recorded expense.
/// </summary>
public sealed partial class InboxProcessor(
    IInboxApi inbox,
    IExpensesApi expenses,
    IExpensesQueries queries,
    Household household,
    ILogger<InboxProcessor> logger)
{
    // The transactable currencies (the add form's set); IRT is display-only.
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.Ordinal) { "AMD", "USD", "USDT" };

    public async Task ProcessAsync(Guid itemId, CancellationToken ct)
    {
        // No household configured yet: every sender would look unknown and be ignored for good. Leave items
        // unsettled; the startup sweep picks them up once Household:Members is set.
        if (household.Members.Count == 0)
        {
            LogNoHousehold(logger, itemId);
            return;
        }

        var item = await inbox.GetAsync(itemId, ct);
        if (item is null || item.Status is "Linked" or "Ignored")
        {
            return;
        }

        var member = household.FindByTelegramUser(item.SenderExternalId);
        if (member is null)
        {
            if (item.Status is "Received" or "Failed")
            {
                await inbox.IgnoreAsync(itemId, ct);
            }

            await ReactAsync(itemId, InboxReactionKind.None, ct);
            return;
        }

        var draft = item.Draft;
        if (item.Status is "Received" or "Failed")
        {
            var parsed = await inbox.ParseAsync(itemId, ct);
            switch (parsed.Status)
            {
                case "Ignored":
                    await ReactAsync(itemId, InboxReactionKind.None, ct);
                    return;
                case "Failed":
                    await ReactAsync(itemId, InboxReactionKind.Failed, ct);
                    return;
            }

            draft = parsed.Draft;
        }

        if (draft is not { Lines.Count: 1 })
        {
            return;
        }

        var line = draft.Lines[0];
        var date = InboxReviewSeed.DateOf(draft, item.SentOnUtc);
        var match = string.IsNullOrWhiteSpace(line.SourceText) ? null : await queries.MatchItemAsync(line.SourceText, ct);

        if (match is not null
            && line.Amount > 0m
            && draft.CurrencyCode is { } currency && SupportedCurrencies.Contains(currency)
            && await queries.CountSameDayMatchesAsync(date, currency, line.Amount, ct) == 0
            && await TryRecordAsync(itemId, currency, date, line.Amount, match, member, ct))
        {
            return;
        }

        // Left parsed: the inbox page lists it for review.
        await ReactAsync(itemId, InboxReactionKind.NeedsReview, ct);
    }

    private async Task<bool> TryRecordAsync(
        Guid itemId, string currency, DateOnly date, decimal amount, ItemMatchDto match, HouseholdMember member, CancellationToken ct)
    {
        Guid expenseId;
        try
        {
            expenseId = await expenses.RecordExpenseAsync(
                new RecordExpenseRequest(
                    currency, date, [new ExpenseLineDto(match.CanonicalDescription, match.CategoryId, amount)], PaidBy: member.Key),
                ct);
        }
        catch (DomainException ex)
        {
            // E.g. the learned category was archived since: let a human decide instead.
            LogAutoRecordRejected(logger, itemId, ex.Message);
            return false;
        }

        await inbox.LinkAsync(itemId, expenseId, ct);

        var category = (await queries.ListActiveCategoriesAsync(ct))
            .FirstOrDefault(c => c.Id == match.CategoryId)?.Name;
        var decimals = currency == "AMD" ? 0 : 2;
        var summary = string.Join(
            " · ",
            new[]
            {
                $"{amount.ToString("N" + decimals, CultureInfo.InvariantCulture)} {currency}",
                match.CanonicalDescription,
                category,
                $"paid by {(string.IsNullOrWhiteSpace(member.DisplayName) ? member.Key : member.DisplayName)}",
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

        await ReactAsync(itemId, InboxReactionKind.Recorded, ct);
        await ReplyAsync(itemId, summary, ct);
        return true;
    }

    private Task ReactAsync(Guid itemId, InboxReactionKind reaction, CancellationToken ct) =>
        BestEffortAsync(() => inbox.ReactAsync(itemId, reaction, ct), itemId);

    private Task ReplyAsync(Guid itemId, string text, CancellationToken ct) =>
        BestEffortAsync(() => inbox.ReplyAsync(itemId, text, ct), itemId);

    private async Task BestEffortAsync(Func<Task> feedback, Guid itemId)
    {
        try
        {
            await feedback();
        }
        catch (HttpRequestException ex)
        {
            LogFeedbackFailed(logger, itemId, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            LogFeedbackFailed(logger, itemId, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox item {ItemId} left unsettled: Household:Members is not configured.")]
    private static partial void LogNoHousehold(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox item {ItemId}: auto-record rejected ({Reason}); sent to review.")]
    private static partial void LogAutoRecordRejected(ILogger logger, Guid itemId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox item {ItemId}: Telegram feedback failed ({Reason}).")]
    private static partial void LogFeedbackFailed(ILogger logger, Guid itemId, string reason);
}
