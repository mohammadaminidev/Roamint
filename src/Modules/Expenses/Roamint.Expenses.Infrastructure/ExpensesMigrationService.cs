using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// Applies pending migrations on startup in Development only, then seeds a starter category set on an
/// empty database so the add-expense form has chips to show before category management exists. The
/// production migration strategy (startup-migrate vs. bundle) is deferred — see 0005.
/// </summary>
internal sealed class ExpensesMigrationService(IServiceProvider services, IHostEnvironment environment)
    : IHostedService
{
    private static readonly string[] StarterCategories =
        ["Food", "Transport", "Rent", "Groceries", "Utilities"];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        await SeedStarterCategoriesAsync(db, scope.ServiceProvider, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedStarterCategoriesAsync(
        ExpensesDbContext db,
        IServiceProvider provider,
        CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct))
        {
            return;
        }

        var clock = provider.GetRequiredService<TimeProvider>();
        foreach (var name in StarterCategories)
        {
            db.Categories.Add(Category.Create(CategoryId.New(clock), name));
        }

        await db.SaveChangesAsync(ct);
    }
}
