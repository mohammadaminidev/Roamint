using System.Diagnostics.CodeAnalysis;
using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Receipts;

/// <summary>
/// Runs OCR over a previously-<see cref="ReceiptStatus.Uploaded"/> receipt: re-opens the stored image, asks the
/// vision adapter for a <see cref="ParsedDraft"/>, and either <see cref="Receipt.Parse"/>s it or
/// <see cref="Receipt.MarkFailed"/>s the receipt. This is the step the host backgrounds (0019); on success it
/// returns the draft so the worker can resolve item matches and seed the review form. OCR stays best-effort — a
/// thrown adapter fails the receipt, not the request, so the user can still record it by hand.
/// </summary>
public sealed record ParseReceipt(ReceiptId ReceiptId) : ICommand<ParsedDraft?>;

internal sealed class ParseReceiptHandler(
    IReceiptRepository receipts,
    IReceiptImageStore images,
    IReceiptOcr ocr,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ParseReceipt, ParsedDraft?>
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "OCR is a best-effort external call; any failure degrades to a Failed receipt the "
            + "user can still complete by hand, rather than surfacing as a request error.")]
    public async Task<ParsedDraft?> HandleAsync(ParseReceipt command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var receipt = await receipts.GetAsync(command.ReceiptId, ct)
            ?? throw new ReceiptNotFoundException(command.ReceiptId);

        try
        {
            var image = await images.OpenAsync(receipt.ImageKey, ct);
            var draft = await ocr.ExtractAsync(image.Bytes, image.ContentType, ct);
            receipt.Parse(draft, clock);
            await unitOfWork.SaveChangesAsync(ct);
            return draft;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            receipt.MarkFailed(ex.Message);
            await unitOfWork.SaveChangesAsync(ct);
            return null;
        }
    }
}
