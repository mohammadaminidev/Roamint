using Roamint.Application.Abstractions;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Inbox;

/// <summary>An inbox item as the host's worker sees it: who sent it, when, its status, and the draft once parsed.
/// <see cref="Status"/> is the canonical <see cref="InboxItemStatus"/> name.</summary>
public sealed record InboxItemView(
    Guid ItemId,
    string Source,
    string? SenderExternalId,
    string Text,
    DateTimeOffset SentOnUtc,
    string Status,
    Guid? LinkedExpenseId,
    DraftView? Draft);

/// <summary>Fetches an inbox item's current state; null if the id is unknown. Read model in Infrastructure.</summary>
public sealed record GetInboxItem(InboxItemId ItemId) : IQuery<InboxItemView?>;
