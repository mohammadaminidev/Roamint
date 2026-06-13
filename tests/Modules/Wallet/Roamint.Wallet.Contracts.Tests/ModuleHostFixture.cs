using Microsoft.Extensions.Hosting;
using Roamint.Wallet.Contracts;
using Testcontainers.PostgreSql;

namespace Roamint.Wallet.Contracts.Tests;

/// <summary>
/// Boots the module the way the web host does — <c>AddWalletModule</c> against a real Postgres reached by the
/// Aspire connection name (<c>roamintdb</c>), with the dev migration hosted service applying the schema on
/// <c>StartAsync</c>. Exercises the full Contracts → dispatcher → handler → EF path, including that the
/// read-model query handler in Infrastructure is discovered by the dispatcher's assembly scan.
/// </summary>
public sealed class ModuleHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
    private IHost? _host;

    public IServiceProvider Services => _host!.Services;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });
        builder.Configuration["ConnectionStrings:roamintdb"] = _container.GetConnectionString();
        builder.AddWalletModule();

        _host = builder.Build();
        await _host.StartAsync(); // runs the dev migration hosted service
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _container.DisposeAsync();
    }
}

[CollectionDefinition(nameof(ModuleHostCollection))]
public sealed class ModuleHostCollection : ICollectionFixture<ModuleHostFixture>;
