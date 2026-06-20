using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Infrastructure;
using Testcontainers.PostgreSql;

namespace Roamint.Receipts.Infrastructure.Tests;

/// <summary>A real Postgres started once per run and migrated up. Each test isolates by unique receipt id.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext(new FakeTimeProvider());
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    internal ReceiptsDbContext CreateContext(TimeProvider clock) =>
        new(new DbContextOptionsBuilder<ReceiptsDbContext>().UseNpgsql(ConnectionString).Options, clock);
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
