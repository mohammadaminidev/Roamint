using Roamint.Application.Abstractions;
using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Contracts;

/// <summary>Dispatches read queries and maps internal projections to the public DTOs.</summary>
internal sealed class ReceiptsQueries(IQueryDispatcher queries) : IReceiptsQueries
{
    public async Task<ReceiptDto?> GetAsync(Guid receiptId, CancellationToken ct = default)
    {
        var view = await queries.QueryAsync(new GetReceipt(new ReceiptId(receiptId)), ct);
        return view is null
            ? null
            : new ReceiptDto(view.ReceiptId, view.Status, view.LinkedExpenseId, Mappings.ToDto(view.Draft));
    }
}
