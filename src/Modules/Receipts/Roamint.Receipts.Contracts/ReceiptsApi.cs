using Roamint.Application.Abstractions;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Contracts;

/// <summary>Translates the public DTOs into the module's commands/queries and dispatches them.</summary>
internal sealed class ReceiptsApi(ICommandDispatcher commands, IQueryDispatcher queries) : IReceiptsApi
{
    public async Task<UploadReceiptResult> UploadAsync(UploadReceiptRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = await commands.SendAsync(new UploadReceipt(request.Image, request.ContentType), ct);

        // OCR ran inside the command; read the persisted draft back for the review screen.
        var view = await queries.QueryAsync(new GetReceipt(id), ct);
        var parsed = view?.Status == nameof(ReceiptStatus.Parsed);
        return new UploadReceiptResult(id.Value, parsed, Mappings.ToDto(view?.Draft));
    }

    public Task LinkAsync(Guid receiptId, Guid expenseId, CancellationToken ct = default) =>
        commands.SendAsync(new LinkReceipt(new ReceiptId(receiptId), expenseId), ct);
}
