using Microsoft.EntityFrameworkCore;
using Roamint.Receipts.Domain;
using Roamint.Receipts.Infrastructure.Outbox;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Infrastructure;

/// <summary>EF Core write model for the <c>receipts</c> schema. Shares the database with the other modules
/// but owns a separate schema (and therefore its own migrations-history table via the default schema).</summary>
internal sealed class ReceiptsDbContext : DbContext
{
    public const string Schema = "receipts";

    private readonly TimeProvider _clock;

    public ReceiptsDbContext(DbContextOptions<ReceiptsDbContext> options, TimeProvider clock)
        : base(options)
    {
        _clock = clock;
    }

    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<ImportedOrder> ImportedOrders => Set<ImportedOrder>();

    public DbSet<InboxItem> InboxItems => Set<InboxItem>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReceiptsDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<ReceiptId>().HaveConversion<ReceiptIdConverter>();
        configurationBuilder.Properties<ImportedOrderId>().HaveConversion<ImportedOrderIdConverter>();
        configurationBuilder.Properties<InboxItemId>().HaveConversion<InboxItemIdConverter>();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CaptureOutboxMessages();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        CaptureOutboxMessages();
        return base.SaveChanges();
    }

    private void CaptureOutboxMessages()
    {
        var now = _clock.GetUtcNow();

        var aggregates = ChangeTracker.Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxMessages.Add(OutboxMessage.From(domainEvent, now));
            }

            aggregate.ClearDomainEvents();
        }
    }
}
