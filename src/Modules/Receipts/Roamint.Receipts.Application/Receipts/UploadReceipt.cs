using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Receipts;

/// <summary>
/// Stores a receipt image and lands a new <see cref="Receipt"/> in the <see cref="ReceiptStatus.Uploaded"/>
/// resting state, returning its id. Fast — one image write and one DB write, no external call. OCR is a
/// separate, backgrounded step (<see cref="ParseReceipt"/>, 0019), so capture returns near-instantly and the
/// user can keep snapping.
/// </summary>
public sealed record UploadReceipt(byte[] Image, string ContentType) : ICommand<ReceiptId>;

internal sealed class UploadReceiptHandler(
    IReceiptRepository receipts,
    IReceiptImageStore images,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UploadReceipt, ReceiptId>
{
    public async Task<ReceiptId> HandleAsync(UploadReceipt command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var imageKey = await images.SaveAsync(command.Image, command.ContentType, ct);

        var receipt = Receipt.Upload(ReceiptId.New(clock), imageKey, clock);
        await receipts.AddAsync(receipt, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return receipt.Id;
    }
}
