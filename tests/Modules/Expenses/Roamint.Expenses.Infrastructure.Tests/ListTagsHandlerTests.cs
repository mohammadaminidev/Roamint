using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class ListTagsHandlerTests(PostgresFixture fixture)
{
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Category_filter_returns_globals_union_scoped_and_excludes_others_and_archived()
    {
        var clock = Clock();
        var token = $"LT{Guid.NewGuid():N}";
        var groceries = CategoryId.New(clock);
        var transport = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Tags.Add(Tag.Create(TagId.New(clock), $"{token} Fruit", groceries));      // scoped to filter
            db.Tags.Add(Tag.Create(TagId.New(clock), $"{token} Gift"));                    // global → included
            db.Tags.Add(Tag.Create(TagId.New(clock), $"{token} Commute", transport));     // other scope → excluded
            var archived = Tag.Create(TagId.New(clock), $"{token} Old", groceries);
            archived.Archive();
            db.Tags.Add(archived);                                                          // archived → excluded
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new ListTagsHandler(db);
            var results = await handler.HandleAsync(
                new ListTags(groceries.Value), TestContext.Current.CancellationToken);

            var mine = results.Where(tag => tag.Name.StartsWith(token, StringComparison.Ordinal)).ToList();

            mine.Select(tag => tag.Name).ShouldBe([$"{token} Fruit", $"{token} Gift"]);
            mine.Single(tag => tag.Name == $"{token} Fruit").Scope.ShouldBe(groceries.Value);
            mine.Single(tag => tag.Name == $"{token} Gift").Scope.ShouldBeNull();
        }
    }

    [Fact]
    public async Task No_filter_returns_all_active_tags()
    {
        var clock = Clock();
        var token = $"LTA{Guid.NewGuid():N}";
        var groceries = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Tags.Add(Tag.Create(TagId.New(clock), $"{token} Fruit", groceries));
            db.Tags.Add(Tag.Create(TagId.New(clock), $"{token} Gift"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new ListTagsHandler(db);
            var results = await handler.HandleAsync(new ListTags(null), TestContext.Current.CancellationToken);

            var mine = results.Where(tag => tag.Name.StartsWith(token, StringComparison.Ordinal)).ToList();
            mine.Select(tag => tag.Name).ShouldBe([$"{token} Fruit", $"{token} Gift"]);
        }
    }
}
