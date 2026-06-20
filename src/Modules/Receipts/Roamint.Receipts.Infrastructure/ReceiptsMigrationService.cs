using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Roamint.Receipts.Infrastructure;

/// <summary>
/// Applies pending migrations on startup in Development only. No seed data. Production migration strategy is
/// deferred (see 0005), matching the other modules.
/// </summary>
internal sealed class ReceiptsMigrationService(IServiceProvider services, IHostEnvironment environment)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
