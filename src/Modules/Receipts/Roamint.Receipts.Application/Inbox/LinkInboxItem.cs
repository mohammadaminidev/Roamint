using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Inbox;

/// <summary>Links a parsed inbox item to the expense recorded from it — automatically by the host worker, or
/// after review on the add-expense form. The link is an opaque expense id — no cross-schema FK.</summary>
public sealed record LinkInboxItem(InboxItemId ItemId, Guid ExpenseId) : ICommand;

internal sealed class LinkInboxItemHandler(
    IInboxItemRepository items,
    IUnitOfWork unitOfWork) : ICommandHandler<LinkInboxItem>
{
    public async Task HandleAsync(LinkInboxItem command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.ItemId, ct)
            ?? throw new InboxItemNotFoundException(command.ItemId);

        item.LinkTo(command.ExpenseId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
