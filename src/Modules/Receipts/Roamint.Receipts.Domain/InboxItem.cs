using Roamint.SharedKernel;

namespace Roamint.Receipts.Domain;

/// <summary>
/// A message drained from the relay inbox (0024) — today a Telegram text from the household group. It is
/// <see cref="InboxItemStatus.Received"/> as-is, then either <see cref="InboxItemStatus.Parsed"/> into a one-line
/// <see cref="ParsedDraft"/> (one message = one payment), <see cref="InboxItemStatus.Ignored"/> (chatter or an
/// unknown sender) or <see cref="InboxItemStatus.Failed"/> (the extractor call failed; re-parseable). A parsed
/// item is <see cref="InboxItemStatus.Linked"/> once its expense is recorded. <see cref="Source"/> +
/// <see cref="ExternalId"/> are the dedup key, so a re-drained message is caught rather than double-spent.
/// </summary>
public sealed class InboxItem : AggregateRoot<InboxItemId>
{
    public const int MaxSourceLength = 32;
    public const int MaxExternalIdLength = 128;
    public const int MaxTextLength = 4096;

    private InboxItem(InboxItemId id) : base(id) { }

    /// <summary>The relay source, e.g. <c>"telegram"</c>. Part of the dedup key.</summary>
    public string Source { get; private set; } = null!;

    /// <summary>The source's own message id (Telegram: <c>"&lt;chat&gt;:&lt;message&gt;"</c>). Also the reply address.</summary>
    public string ExternalId { get; private set; } = null!;

    /// <summary>The sender's id at the source (Telegram user id), mapped to a household member by the host.</summary>
    public string? SenderExternalId { get; private set; }

    public string Text { get; private set; } = null!;

    /// <summary>When the message was sent — the anchor for "tonight", never the drain time.</summary>
    public DateTimeOffset SentOnUtc { get; private set; }

    public DateTimeOffset ReceivedOnUtc { get; private set; }

    /// <summary>The raw relay payload, kept so a message can be re-read if the extraction ever changes.</summary>
    public string Payload { get; private set; } = null!;

    public InboxItemStatus Status { get; private set; }

    /// <summary>Set on <see cref="Parse"/>: exactly one line.</summary>
    public ParsedDraft? Draft { get; private set; }

    /// <summary>The recorded expense, set on <see cref="LinkTo"/>. Opaque — Receipts owns no Expenses types.</summary>
    public Guid? LinkedExpenseId { get; private set; }

    public static InboxItem Receive(
        InboxItemId id,
        string source,
        string externalId,
        string? senderExternalId,
        string text,
        DateTimeOffset sentOnUtc,
        string payload,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(source) || source.Length > MaxSourceLength)
        {
            throw new DomainException($"An inbox item must name its source ({MaxSourceLength} characters or fewer).");
        }

        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > MaxExternalIdLength)
        {
            throw new DomainException(
                $"An inbox item must carry the source's message id ({MaxExternalIdLength} characters or fewer).");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainException("An inbox item must carry message text.");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new DomainException("An inbox item must keep its raw payload.");
        }

        var now = clock.GetUtcNow();
        var item = new InboxItem(id)
        {
            Source = source,
            ExternalId = externalId,
            SenderExternalId = string.IsNullOrWhiteSpace(senderExternalId) ? null : senderExternalId,
            Text = text.Length > MaxTextLength ? text[..MaxTextLength] : text,
            SentOnUtc = sentOnUtc,
            ReceivedOnUtc = now,
            Payload = payload,
            Status = InboxItemStatus.Received,
        };
        item.Raise(new InboxItemReceived(id, source, externalId, now));
        return item;
    }

    public void Parse(ParsedDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireReadable("parse");

        if (draft.Lines.Count != 1)
        {
            throw new DomainException("A parsed inbox item holds exactly one line: one message is one payment.");
        }

        Draft = draft;
        Status = InboxItemStatus.Parsed;
    }

    public void Ignore()
    {
        RequireReadable("ignore");
        Status = InboxItemStatus.Ignored;
    }

    public void MarkFailed()
    {
        RequireReadable("fail");
        Status = InboxItemStatus.Failed;
    }

    public void LinkTo(Guid expenseId)
    {
        if (expenseId == Guid.Empty)
        {
            throw new DomainException("An inbox item must link to a real expense.");
        }

        if (Status != InboxItemStatus.Parsed)
        {
            throw new DomainException($"Cannot link an inbox item in status {Status}; expected {InboxItemStatus.Parsed}.");
        }

        LinkedExpenseId = expenseId;
        Status = InboxItemStatus.Linked;
        Raise(new InboxItemLinked(Id, expenseId));
    }

    // Reading (parse / ignore / fail) happens on a fresh item or retries a failed one.
    private void RequireReadable(string action)
    {
        if (Status is not (InboxItemStatus.Received or InboxItemStatus.Failed))
        {
            throw new DomainException(
                $"Cannot {action} an inbox item in status {Status}; expected {InboxItemStatus.Received} or {InboxItemStatus.Failed}.");
        }
    }
}
