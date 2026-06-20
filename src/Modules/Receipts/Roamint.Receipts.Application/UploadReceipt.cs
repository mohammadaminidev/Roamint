using System.Diagnostics.CodeAnalysis;
using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application;

/// <summary>
/// Stores a receipt image, runs OCR synchronously, and lands the result as a <see cref="ParsedDraft"/> on a
/// new <see cref="Receipt"/> — or marks the receipt failed if OCR could not run. Returns the receipt's id;
/// the host reads the draft back via <see cref="GetReceipt"/> to pre-fill the review screen. OCR is
/// best-effort: a thrown adapter fails the receipt, not the request, so the user can still record by hand.
/// </summary>
public sealed record UploadReceipt(byte[] Image, string ContentType) : ICommand<ReceiptId>;

internal sealed class UploadReceiptHandler(
    IReceiptRepository receipts,
    IReceiptImageStore images,
    IReceiptOcr ocr,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UploadReceipt, ReceiptId>
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "OCR is a best-effort external call; any failure degrades to a Failed receipt the "
            + "user can still complete by hand, rather than surfacing as a request error.")]
    public async Task<ReceiptId> HandleAsync(UploadReceipt command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var imageKey = await images.SaveAsync(command.Image, command.ContentType, ct);

        var receipt = Receipt.Upload(ReceiptId.New(clock), imageKey, clock);
        await receipts.AddAsync(receipt, ct);

        try
        {
            var draft = await ocr.ExtractAsync(command.Image, command.ContentType, ct);
            receipt.Parse(draft, clock);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            receipt.MarkFailed(ex.Message);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return receipt.Id;
    }
}
