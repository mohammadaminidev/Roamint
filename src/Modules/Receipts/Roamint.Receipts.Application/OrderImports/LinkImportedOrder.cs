using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.OrderImports;

/// <summary>Links an imported order to the expense the user recorded from it. The web host calls this after a
/// successful RecordExpense; the link is an opaque expense id — no cross-schema FK.</summary>
public sealed record LinkImportedOrder(ImportedOrderId OrderId, Guid ExpenseId) : ICommand;

internal sealed class LinkImportedOrderHandler(
    IImportedOrderRepository orders,
    IUnitOfWork unitOfWork) : ICommandHandler<LinkImportedOrder>
{
    public async Task HandleAsync(LinkImportedOrder command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await orders.GetAsync(command.OrderId, ct)
            ?? throw new ImportedOrderNotFoundException(command.OrderId);

        order.LinkTo(command.ExpenseId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
