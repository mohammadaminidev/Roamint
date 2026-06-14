using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class ListActiveCategoriesHandlerTests(PostgresFixture fixture)
{
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Returns_active_categories_name_ordered_and_excludes_archived()
    {
        // The fixture's Postgres is shared across the collection, so scope assertions to a unique tag
        // rather than assuming the table holds only these rows.
        var tag = $"LAC-{Guid.NewGuid():N}";
        var clock = Clock();

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(CategoryId.New(clock), $"{tag} Bravo"));
            db.Categories.Add(Category.Create(CategoryId.New(clock), $"{tag} Alpha"));

            var archived = Category.Create(CategoryId.New(clock), $"{tag} Zulu");
            archived.Archive();
            db.Categories.Add(archived);

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new ListActiveCategoriesHandler(db);
            var all = await handler.HandleAsync(new ListActiveCategories(), TestContext.Current.CancellationToken);

            var mine = all.Where(category => category.Name.StartsWith(tag, StringComparison.Ordinal)).ToList();

            mine.Select(category => category.Name).ShouldBe([$"{tag} Alpha", $"{tag} Bravo"]);
            mine.ShouldAllBe(category => category.Id != Guid.Empty);
        }
    }
}
