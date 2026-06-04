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

    // Anonymized starter tags (0009 seed set): scoped sub-classifications of Groceries, plus globals.
    private const string ScopedTagCategory = "Groceries";
    private static readonly string[] ScopedStarterTags = ["Fruit", "Vegetables", "Dairy"];
    private static readonly string[] GlobalStarterTags = ["For Happiness", "Gift", "Essential"];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await db.Database.MigrateAsync(cancellationToken);

        var categories = await SeedStarterCategoriesAsync(db, clock, cancellationToken);
        await SeedStarterTagsAsync(db, clock, categories, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<IReadOnlyDictionary<string, Category>> SeedStarterCategoriesAsync(
        ExpensesDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct))
        {
            return await db.Categories.ToDictionaryAsync(category => category.Name, ct);
        }

        var created = StarterCategories.ToDictionary(
            name => name,
            name => Category.Create(CategoryId.New(clock), name));
        db.Categories.AddRange(created.Values);
        await db.SaveChangesAsync(ct);
        return created;
    }

    private static async Task SeedStarterTagsAsync(
        ExpensesDbContext db,
        TimeProvider clock,
        IReadOnlyDictionary<string, Category> categories,
        CancellationToken ct)
    {
        if (await db.Tags.AnyAsync(ct))
        {
            return;
        }

        if (categories.TryGetValue(ScopedTagCategory, out var groceries))
        {
            foreach (var name in ScopedStarterTags)
            {
                db.Tags.Add(Tag.Create(TagId.New(clock), name, groceries.Id));
            }
        }

        foreach (var name in GlobalStarterTags)
        {
            db.Tags.Add(Tag.Create(TagId.New(clock), name, scope: null));
        }

        await db.SaveChangesAsync(ct);
    }
}
