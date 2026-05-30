using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// Applies pending migrations on startup in Development only. The production migration strategy
/// (startup-migrate vs. bundle) is deferred — see 0005.
/// </summary>
internal sealed class ExpensesMigrationService(IServiceProvider services, IHostEnvironment environment)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
