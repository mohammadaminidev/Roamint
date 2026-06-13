using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Wallet.Infrastructure;
using Testcontainers.PostgreSql;

namespace Roamint.Wallet.Infrastructure.Tests;

/// <summary>
/// A real Postgres started once per run and migrated up. Shared across the collection; exchange/outbox tests
/// isolate by unique ids, while rate tests clear <c>exchange_rates</c> (its PK is the currency pair, so it
/// is not naturally unique per test).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext(new FakeTimeProvider());
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    internal WalletDbContext CreateContext(TimeProvider clock) =>
        new(new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(ConnectionString).Options, clock);
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
