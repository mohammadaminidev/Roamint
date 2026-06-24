using Microsoft.EntityFrameworkCore;
using Roamint.Application.Abstractions;
using Roamint.Receipts.Application.Receipts;

namespace Roamint.Receipts.Infrastructure;

/// <summary>
/// Read model for the review screen. Materializes the receipt (the OCR draft is a converted jsonb column, so
/// it is read off the loaded aggregate rather than projected in SQL) and maps it to the view shape.
/// </summary>
internal sealed class GetReceiptHandler(ReceiptsDbContext db)
    : IQueryHandler<GetReceipt, ReceiptView?>
{
    public async Task<ReceiptView?> HandleAsync(GetReceipt query, CancellationToken ct)
    {
        var receipt = await db.Receipts.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == query.ReceiptId, ct);

        if (receipt is null)
        {
            return null;
        }

        var draft = receipt.Draft is null
            ? null
            : new DraftView(
                receipt.Draft.Merchant,
                receipt.Draft.Date,
                receipt.Draft.CurrencyCode,
                receipt.Draft.Lines
                    .Select(line => new DraftLineView(
                        line.Description, line.Amount, line.QuantityValue, line.QuantityUnit, line.SourceText))
                    .ToList());

        return new ReceiptView(
            receipt.Id.Value,
            receipt.Status.ToString(),
            receipt.LinkedExpenseId,
            draft);
    }
}
