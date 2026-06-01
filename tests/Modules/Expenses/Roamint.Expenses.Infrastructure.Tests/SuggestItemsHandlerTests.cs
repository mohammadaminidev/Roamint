using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class SuggestItemsHandlerTests(PostgresFixture fixture)
{
    private static readonly Currency Usd = Currency.Usd;

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Unions_notes_and_lines_dedupes_carries_most_recent_category_and_ranks_prefix_first()
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

            // Same note twice — must de-dupe; the later (food) occurrence wins the carried category.
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), cafe, Money.Of(3m, Usd), new DateOnly(2026, 4, 10), clock, note: $"{tag} Coffee"));
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), food, Money.Of(3m, Usd), new DateOnly(2026, 4, 20), clock, note: $"{tag} Coffee"));

            // A contains-but-not-prefix note (the tag sits mid-string).
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), cafe, Money.Of(4m, Usd), new DateOnly(2026, 4, 21), clock, note: $"Morning {tag} Coffee"));

            // A line description joins the same corpus.
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), food, Money.Of(5m, Usd), new DateOnly(2026, 4, 22), clock,
                lines: [new ExpenseLineDraft($"{tag} Croissant", 1m, Money.Of(5m, Usd))]));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new SuggestItemsHandler(db);
            var results = await handler.HandleAsync(new SuggestItems(tag, 10), TestContext.Current.CancellationToken);

            var texts = results.Select(r => r.Text).ToList();

            // De-duped across the two identical notes.
            texts.Count(t => t == $"{tag} Coffee").ShouldBe(1);

            // Both corpora present (note + line description).
            texts.ShouldContain($"{tag} Coffee");
            texts.ShouldContain($"{tag} Croissant");

            // Carried category is the most recent use of that text (food, 04-20 > cafe, 04-10).
            results.Single(r => r.Text == $"{tag} Coffee").CategoryId.ShouldBe(food.Value);

            // Prefix matches rank ahead of the contains-only "Morning …" entry.
            var firstContains = texts.IndexOf($"Morning {tag} Coffee");
            firstContains.ShouldBeGreaterThan(texts.IndexOf($"{tag} Coffee"));
            firstContains.ShouldBeGreaterThan(texts.IndexOf($"{tag} Croissant"));
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
                    ExpenseId.New(clock), category, Money.Of(1m, Usd), new DateOnly(2026, 4, 10), clock,
                    note: $"{tag} item {i}"));
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
