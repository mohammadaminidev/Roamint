using Roamint.Application.Abstractions;
using Roamint.Receipts.Application;
using Roamint.Receipts.Application.Inbox;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Contracts;

/// <summary>Translates the public inbox DTOs into the module's commands/queries and dispatches them.</summary>
internal sealed class InboxApi(ICommandDispatcher commands, IQueryDispatcher queries) : IInboxApi
{
    public async Task<IReadOnlyList<Guid>> AcceptAsync(
        IReadOnlyList<InboxEnvelopeDto> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        var ids = await commands.SendAsync(
            new AcceptInboxEnvelopes(envelopes
                .Select(envelope => new InboxEnvelope(envelope.Source, envelope.ExternalId, envelope.Payload))
                .ToList()),
            ct);
        return ids.Select(id => id.Value).ToList();
    }

    public async Task<ParseInboxItemResult> ParseAsync(Guid itemId, CancellationToken ct = default)
    {
        var outcome = await commands.SendAsync(new ParseInboxItem(new InboxItemId(itemId)), ct);
        return new ParseInboxItemResult(
            outcome.Status.ToString(), outcome.Draft is null ? null : Mappings.ToDto(outcome.Draft));
    }

    public Task IgnoreAsync(Guid itemId, CancellationToken ct = default) =>
        commands.SendAsync(new IgnoreInboxItem(new InboxItemId(itemId)), ct);

    public Task LinkAsync(Guid itemId, Guid expenseId, CancellationToken ct = default) =>
        commands.SendAsync(new LinkInboxItem(new InboxItemId(itemId), expenseId), ct);

    public Task ReactAsync(Guid itemId, InboxReactionKind reaction, CancellationToken ct = default) =>
        commands.SendAsync(new ReactToInboxItem(new InboxItemId(itemId), ToReaction(reaction)), ct);

    public Task ReplyAsync(Guid itemId, string text, CancellationToken ct = default) =>
        commands.SendAsync(new ReplyToInboxItem(new InboxItemId(itemId), text), ct);

    public async Task<InboxItemDto?> GetAsync(Guid itemId, CancellationToken ct = default)
    {
        var view = await queries.QueryAsync(new GetInboxItem(new InboxItemId(itemId)), ct);
        return view is null
            ? null
            : new InboxItemDto(
                view.ItemId,
                view.Source,
                view.SenderExternalId,
                view.Text,
                view.SentOnUtc,
                view.Status,
                view.LinkedExpenseId,
                Mappings.ToDto(view.Draft));
    }

    public Task<IReadOnlyList<Guid>> ListOpenAsync(CancellationToken ct = default) =>
        queries.QueryAsync(new ListOpenInboxItems(), ct);

    private static InboxReaction ToReaction(InboxReactionKind kind) => kind switch
    {
        InboxReactionKind.Recorded => InboxReaction.Recorded,
        InboxReactionKind.NeedsReview => InboxReaction.NeedsReview,
        InboxReactionKind.Failed => InboxReaction.Failed,
        _ => InboxReaction.None,
    };
}
