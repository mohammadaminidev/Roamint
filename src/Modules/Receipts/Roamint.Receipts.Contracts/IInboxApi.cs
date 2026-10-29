namespace Roamint.Receipts.Contracts;

/// <summary>
/// The Receipts module's inbox API (0024): messages drained from the relay become inbox items, are read into a
/// one-line draft, and are linked to the expense recorded from them. Feedback (reaction, reply) goes back to the
/// source through here, because only this module knows an item's source and reply address. The host orchestrates;
/// matching and recording live in Expenses.
/// </summary>
public interface IInboxApi
{
    /// <summary>Stores drained relay envelopes; idempotent on (source, external id). Returns the ids of newly
    /// accepted items. Unreadable envelopes are skipped, so the host can ack every envelope it passed in.</summary>
    Task<IReadOnlyList<Guid>> AcceptAsync(IReadOnlyList<InboxEnvelopeDto> envelopes, CancellationToken ct = default);

    /// <summary>Reads a received (or failed) item into a one-line draft, or marks it ignored (not an expense) or
    /// failed (extractor unavailable).</summary>
    Task<ParseInboxItemResult> ParseAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Marks an item as not to be recorded: a sender who isn't a household member, or a message the user
    /// dismissed from review (0026). A linked item can't be ignored.</summary>
    Task IgnoreAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Links a parsed item to the expense recorded from it.</summary>
    Task LinkAsync(Guid itemId, Guid expenseId, CancellationToken ct = default);

    /// <summary>Sets the item's status at its source (Telegram: the bot's reaction). Throws on transport failure;
    /// callers treat feedback as best-effort.</summary>
    Task ReactAsync(Guid itemId, InboxReactionKind reaction, CancellationToken ct = default);

    /// <summary>Replies to the item in its thread at the source. Throws on transport failure.</summary>
    Task ReplyAsync(Guid itemId, string text, CancellationToken ct = default);

    /// <summary>An item's current state; null if the id is unknown.</summary>
    Task<InboxItemDto?> GetAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Unsettled items (received, awaiting review, failed), oldest first — for the inbox page (0026) and
    /// the startup sweep.</summary>
    Task<IReadOnlyList<InboxItemDto>> ListOpenAsync(CancellationToken ct = default);
}

/// <summary>One drained relay row: source tag, the source's message id, and the raw stored payload.</summary>
public sealed record InboxEnvelopeDto(string Source, string ExternalId, string Payload);

/// <summary>The status an item shows at its source. <see cref="None"/> clears it (e.g. chatter).</summary>
public enum InboxReactionKind
{
    None,
    Recorded,
    NeedsReview,
    Failed,
}

/// <summary>Outcome of reading an item: <see cref="Status"/> is <c>Parsed</c>, <c>Ignored</c> or <c>Failed</c>;
/// <see cref="Draft"/> holds the one-line draft when parsed.</summary>
public sealed record ParseInboxItemResult(string Status, ParsedDraftDto? Draft);

/// <summary>An inbox item's current state. <see cref="Status"/> is the canonical name
/// (<c>Received</c>/<c>Parsed</c>/<c>Ignored</c>/<c>Failed</c>/<c>Linked</c>).</summary>
public sealed record InboxItemDto(
    Guid ItemId,
    string Source,
    string? SenderExternalId,
    string Text,
    DateTimeOffset SentOnUtc,
    string Status,
    Guid? LinkedExpenseId,
    ParsedDraftDto? Draft);
