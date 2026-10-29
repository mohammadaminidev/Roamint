using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Inbox;

/// <summary>Replies to an item at its source, in its thread — used once per auto-recorded item to show what was
/// understood (0024), since the amount came from free text.</summary>
public sealed record ReplyToInboxItem(InboxItemId ItemId, string Text) : ICommand;

internal sealed class ReplyToInboxItemHandler(
    IInboxItemRepository items,
    IInboxNotifier notifier) : ICommandHandler<ReplyToInboxItem>
{
    public async Task HandleAsync(ReplyToInboxItem command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Text);

        var item = await items.GetAsync(command.ItemId, ct)
            ?? throw new InboxItemNotFoundException(command.ItemId);

        await notifier.ReplyAsync(item, command.Text, ct);
    }
}
