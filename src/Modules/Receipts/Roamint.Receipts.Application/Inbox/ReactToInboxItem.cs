using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Inbox;

/// <summary>Sets the status an item shows at its source (0024) — on Telegram, the bot's single reaction on the
/// message, which replaces the relay's 👀. No state change here; the notifier is best-effort.</summary>
public sealed record ReactToInboxItem(InboxItemId ItemId, InboxReaction Reaction) : ICommand;

internal sealed class ReactToInboxItemHandler(
    IInboxItemRepository items,
    IInboxNotifier notifier) : ICommandHandler<ReactToInboxItem>
{
    public async Task HandleAsync(ReactToInboxItem command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.ItemId, ct)
            ?? throw new InboxItemNotFoundException(command.ItemId);

        await notifier.ReactAsync(item, command.Reaction, ct);
    }
}
