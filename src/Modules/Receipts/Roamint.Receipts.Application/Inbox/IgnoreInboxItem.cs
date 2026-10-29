using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Inbox;

/// <summary>Marks an inbox item as not to be recorded — the host does this for a sender who isn't a household
/// member (before spending an extractor call on the message), and the user does it by dismissing a message from
/// review (0026).</summary>
public sealed record IgnoreInboxItem(InboxItemId ItemId) : ICommand;

internal sealed class IgnoreInboxItemHandler(
    IInboxItemRepository items,
    IUnitOfWork unitOfWork) : ICommandHandler<IgnoreInboxItem>
{
    public async Task HandleAsync(IgnoreInboxItem command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.ItemId, ct)
            ?? throw new InboxItemNotFoundException(command.ItemId);

        item.Ignore();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
