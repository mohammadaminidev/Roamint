using Microsoft.EntityFrameworkCore;
using Roamint.Expenses.Domain;
using Roamint.Expenses.Infrastructure.Outbox;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure;

/// <summary>EF Core write model for the <c>expenses</c> schema. Reads use Dapper in a later slice.</summary>
internal sealed class ExpensesDbContext : DbContext
{
    public const string Schema = "expenses";

    private readonly TimeProvider _clock;

    public ExpensesDbContext(DbContextOptions<ExpensesDbContext> options, TimeProvider clock)
        : base(options)
    {
        _clock = clock;
    }

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Expense> Expenses => Set<Expense>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExpensesDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<CategoryId>().HaveConversion<CategoryIdConverter>();
        configurationBuilder.Properties<ExpenseId>().HaveConversion<ExpenseIdConverter>();
        configurationBuilder.Properties<ExpenseLineId>().HaveConversion<ExpenseLineIdConverter>();
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
