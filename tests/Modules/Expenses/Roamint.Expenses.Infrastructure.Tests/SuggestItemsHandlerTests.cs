using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class SuggestItemsHandlerTests(PostgresFixture fixture)
{
    private static readonly Currency Usd = Currency.Usd;

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    private static ExpenseLineDraft Draft(string description, CategoryId category, Quantity? quantity = null) =>
        new(description, category, Money.Of(3m, Usd), quantity);

    [Fact]
    public async Task Dedupes_lines_carries_most_recent_category_and_unit_and_ranks_prefix_first()
    {
        // Shared Postgres: a unique token keeps the ILIKE scan to this test's rows.
        var tag = $"SI{Guid.NewGuid():N}";
        var clock = Clock();

        var cafe = CategoryId.New(clock);
        var food = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(cafe, $"{tag} Cafe"));
            db.Categories.Add(Category.Create(food, $"{tag} Food"));

            // Same line text twice — must de-dupe; the later (food) occurrence wins the carried category + unit.
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 10), [Draft($"{tag} Coffee", cafe)], clock));
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 20),
                [Draft($"{tag} Coffee", food, Quantity.Of(250m, QuantityUnit.Milliliters))], clock));

            // A contains-but-not-prefix line (the tag sits mid-string).
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 21), [Draft($"Morning {tag} Coffee", cafe)], clock));

            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 22),
                [Draft($"{tag} Croissant", food, Quantity.Of(1m, QuantityUnit.Pieces))], clock));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new SuggestItemsHandler(db);
            var results = await handler.HandleAsync(new SuggestItems(tag, 10), TestContext.Current.CancellationToken);

            var texts = results.Select(r => r.Text).ToList();

            texts.Count(t => t == $"{tag} Coffee").ShouldBe(1);
            texts.ShouldContain($"{tag} Coffee");
            texts.ShouldContain($"{tag} Croissant");

            // Carried category + unit are the most recent use of that text (food, 04-20 > cafe, 04-10).
            var coffee = results.Single(r => r.Text == $"{tag} Coffee");
            coffee.CategoryId.ShouldBe(food.Value);
            coffee.Unit.ShouldBe("ml");

            // No-quantity history → null unit.
            results.Single(r => r.Text == $"Morning {tag} Coffee").Unit.ShouldBeNull();

            // Prefix matches rank ahead of the contains-only "Morning …" entry.
            var firstContains = texts.IndexOf($"Morning {tag} Coffee");
            firstContains.ShouldBeGreaterThan(texts.IndexOf($"{tag} Coffee"));
            firstContains.ShouldBeGreaterThan(texts.IndexOf($"{tag} Croissant"));
        }
    }

    [Fact]
    public async Task Carries_remembered_tags_from_the_most_recent_line()
    {
        var tag = $"SIT{Guid.NewGuid():N}";
        var clock = Clock();
        var groceries = CategoryId.New(clock);
        var fruit = TagId.New(clock);
        var happiness = TagId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(groceries, $"{tag} Groceries"));
            db.Tags.Add(Tag.Create(fruit, $"{tag} Fruit", groceries));
            db.Tags.Add(Tag.Create(happiness, $"{tag} Happiness"));

            // Earlier occurrence with one tag; later occurrence wins with two — those are remembered.
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 10),
                [new ExpenseLineDraft($"{tag} Apple", groceries, Money.Of(2m, Usd), null, new HashSet<TagId> { fruit })],
                clock));
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 20),
                [new ExpenseLineDraft($"{tag} Apple", groceries, Money.Of(2m, Usd), null, new HashSet<TagId> { fruit, happiness })],
                clock));

            // A different item with no tags → empty remembered set.
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 21), [Draft($"{tag} Plain", groceries)], clock));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new SuggestItemsHandler(db);
            var results = await handler.HandleAsync(new SuggestItems(tag, 10), TestContext.Current.CancellationToken);

            var apple = results.Single(r => r.Text == $"{tag} Apple");
            apple.TagIds.ShouldBe([fruit.Value, happiness.Value], ignoreOrder: true);

            results.Single(r => r.Text == $"{tag} Plain").TagIds.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Caps_results_at_the_requested_limit()
    {
        var tag = $"SIL{Guid.NewGuid():N}";
        var clock = Clock();
        var category = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(category, $"{tag} Cat"));
            for (var i = 0; i < 5; i++)
            {
                db.Expenses.Add(Expense.Record(
                    ExpenseId.New(clock), new DateOnly(2026, 4, 10), [Draft($"{tag} item {i}", category)], clock));
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new SuggestItemsHandler(db);
            var results = await handler.HandleAsync(new SuggestItems(tag, 3), TestContext.Current.CancellationToken);

            results.Count.ShouldBe(3);
        }
    }
}
