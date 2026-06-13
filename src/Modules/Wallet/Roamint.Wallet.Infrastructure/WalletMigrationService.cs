using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Roamint.Wallet.Infrastructure;

/// <summary>
/// Applies pending migrations on startup in Development only. No seed data — rates start empty and are
/// populated by the user's first exchange or a manual entry. Production migration strategy is deferred
/// (see 0005), matching the Expenses module.
/// </summary>
internal sealed class WalletMigrationService(IServiceProvider services, IHostEnvironment environment)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
