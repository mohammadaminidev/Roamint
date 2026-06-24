using Microsoft.EntityFrameworkCore;
using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure;

internal sealed class ReceiptRepository(ReceiptsDbContext db) : IReceiptRepository
{
    public Task<Receipt?> GetAsync(ReceiptId id, CancellationToken ct) =>
        db.Receipts.FirstOrDefaultAsync(receipt => receipt.Id == id, ct);

    public Task AddAsync(Receipt receipt, CancellationToken ct)
    {
        // IDs are app-assigned (Guid v7), so no async value generation — plain Add suffices.
        db.Receipts.Add(receipt);
        return Task.CompletedTask;
    }
}

internal sealed class ImportedOrderRepository(ReceiptsDbContext db) : IImportedOrderRepository
{
    public Task<ImportedOrder?> GetAsync(ImportedOrderId id, CancellationToken ct) =>
        db.ImportedOrders.FirstOrDefaultAsync(order => order.Id == id, ct);

    public Task<ImportedOrder?> FindAsync(string source, string externalOrderId, CancellationToken ct) =>
        db.ImportedOrders.FirstOrDefaultAsync(
            order => order.Source == source && order.ExternalOrderId == externalOrderId, ct);

    public Task AddAsync(ImportedOrder order, CancellationToken ct)
    {
        db.ImportedOrders.Add(order);
        return Task.CompletedTask;
    }
}

internal sealed class UnitOfWork(ReceiptsDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
