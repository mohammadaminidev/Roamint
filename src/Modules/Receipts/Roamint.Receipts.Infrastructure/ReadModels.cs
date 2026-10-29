using Microsoft.EntityFrameworkCore;
using Roamint.Application.Abstractions;
using Roamint.Receipts.Application.Inbox;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

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

/// <summary>Lists the ids of receipts still in <see cref="Roamint.Receipts.Domain.ReceiptStatus.Uploaded"/>
/// for the host's startup re-enqueue sweep (0019).</summary>
internal sealed class ListPendingReceiptsHandler(ReceiptsDbContext db)
    : IQueryHandler<ListPendingReceipts, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(ListPendingReceipts query, CancellationToken ct)
    {
        var ids = await db.Receipts.AsNoTracking()
            .Where(r => r.Status == Roamint.Receipts.Domain.ReceiptStatus.Uploaded)
            .OrderBy(r => r.UploadedOnUtc)
            .Select(r => r.Id.Value)
            .ToListAsync(ct);

        return ids;
    }
}

/// <summary>Read model for the host's inbox worker (0024). Materializes the item (the draft is a converted jsonb
/// column) and maps it to the view.</summary>
internal sealed class GetInboxItemHandler(ReceiptsDbContext db) : IQueryHandler<GetInboxItem, InboxItemView?>
{
    public async Task<InboxItemView?> HandleAsync(GetInboxItem query, CancellationToken ct)
    {
        var item = await db.InboxItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == query.ItemId, ct);
        return item is null ? null : InboxItemViews.From(item);
    }
}

/// <summary>Unsettled inbox items (received, parsed-awaiting-review, failed), oldest first: the inbox page (0026)
/// and the host's startup sweep (0024). Materialized, since the draft is a converted jsonb column.</summary>
internal sealed class ListOpenInboxItemsHandler(ReceiptsDbContext db)
    : IQueryHandler<ListOpenInboxItems, IReadOnlyList<InboxItemView>>
{
    public async Task<IReadOnlyList<InboxItemView>> HandleAsync(ListOpenInboxItems query, CancellationToken ct)
    {
        var items = await db.InboxItems.AsNoTracking()
            .Where(item => item.Status == InboxItemStatus.Received
                || item.Status == InboxItemStatus.Parsed
                || item.Status == InboxItemStatus.Failed)
            .OrderBy(item => item.SentOnUtc)
            .ToListAsync(ct);
        return items.Select(InboxItemViews.From).ToList();
    }
}

internal static class InboxItemViews
{
    public static InboxItemView From(InboxItem item)
    {
        var draft = item.Draft is null
            ? null
            : new DraftView(
                item.Draft.Merchant,
                item.Draft.Date,
                item.Draft.CurrencyCode,
                item.Draft.Lines
                    .Select(line => new DraftLineView(
                        line.Description, line.Amount, line.QuantityValue, line.QuantityUnit, line.SourceText))
                    .ToList());

        return new InboxItemView(
            item.Id.Value,
            item.Source,
            item.SenderExternalId,
            item.Text,
            item.SentOnUtc,
            item.Status.ToString(),
            item.LinkedExpenseId,
            draft);
    }
}
