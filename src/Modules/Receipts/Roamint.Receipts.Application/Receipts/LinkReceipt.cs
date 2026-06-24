using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Receipts;

/// <summary>Links a parsed receipt to the expense the user recorded from it. The web host calls this after a
/// successful RecordExpense; the link is an opaque expense id — no cross-schema FK.</summary>
public sealed record LinkReceipt(ReceiptId ReceiptId, Guid ExpenseId) : ICommand;

internal sealed class LinkReceiptHandler(
    IReceiptRepository receipts,
    IUnitOfWork unitOfWork) : ICommandHandler<LinkReceipt>
{
    public async Task HandleAsync(LinkReceipt command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var receipt = await receipts.GetAsync(command.ReceiptId, ct)
            ?? throw new ReceiptNotFoundException(command.ReceiptId);

        receipt.LinkTo(command.ExpenseId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
