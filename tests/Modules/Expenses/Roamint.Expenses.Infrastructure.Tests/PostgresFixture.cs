using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Infrastructure;
using Testcontainers.PostgreSql;

namespace Roamint.Expenses.Infrastructure.Tests;

/// <summary>
/// A real Postgres (same image as production) started once per test run and migrated up. Shared across the
/// collection — tests isolate themselves by minting unique aggregate ids rather than by resetting the db.
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

    internal ExpensesDbContext CreateContext(TimeProvider clock) =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(ConnectionString).Options, clock);
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
