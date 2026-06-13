using Microsoft.EntityFrameworkCore;
using Roamint.SharedKernel;
using Roamint.Wallet.Domain;
using Roamint.Wallet.Infrastructure.Outbox;

namespace Roamint.Wallet.Infrastructure;

/// <summary>EF Core write model for the <c>wallet</c> schema. Shares the database with the Expenses module
/// but owns a separate schema (and therefore its own migrations-history table via the default schema).</summary>
internal sealed class WalletDbContext : DbContext
{
    public const string Schema = "wallet";

    private readonly TimeProvider _clock;

    public WalletDbContext(DbContextOptions<WalletDbContext> options, TimeProvider clock)
        : base(options)
    {
        _clock = clock;
    }

    public DbSet<Exchange> Exchanges => Set<Exchange>();

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<ExchangeId>().HaveConversion<ExchangeIdConverter>();
        configurationBuilder.Properties<CurrencyPair>().HaveConversion<CurrencyPairConverter>().HaveMaxLength(16);
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
