using Roamint.Application.Abstractions;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Contracts;

/// <summary>Translates the public DTOs into the module's commands/queries and dispatches them.</summary>
internal sealed class ReceiptsApi(ICommandDispatcher commands) : IReceiptsApi
{
    public async Task<Guid> UploadAsync(UploadReceiptRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = await commands.SendAsync(new UploadReceipt(request.Image, request.ContentType), ct);
        return id.Value;
    }

    public async Task<ParseReceiptResult> ParseAsync(Guid receiptId, CancellationToken ct = default)
    {
        var draft = await commands.SendAsync(new ParseReceipt(new ReceiptId(receiptId)), ct);
        return new ParseReceiptResult(draft is not null, draft is null ? null : Mappings.ToDto(draft));
    }

    public Task LinkAsync(Guid receiptId, Guid expenseId, CancellationToken ct = default) =>
        commands.SendAsync(new LinkReceipt(new ReceiptId(receiptId), expenseId), ct);
}
