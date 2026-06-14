using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class SuggestCategoryHandlerTests(PostgresFixture fixture)
{
    private static readonly Currency Usd = Currency.Usd;

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    private static ExpenseLineDraft Draft(string description, CategoryId category) =>
        new(description, category, Money.Of(2m, Usd));

    [Fact]
    public async Task Returns_the_most_recently_used_category_among_matches()
    {
        var tag = $"SC{Guid.NewGuid():N}";
        var clock = Clock();
        var older = CategoryId.New(clock);
        var newer = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(older, $"{tag} Older"));
            db.Categories.Add(Category.Create(newer, $"{tag} Newer"));

            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 1), [Draft($"{tag} latte", older)], clock));
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), new DateOnly(2026, 4, 15), [Draft($"{tag} latte", newer)], clock));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new SuggestCategoryHandler(db);
            var result = await handler.HandleAsync(new SuggestCategory($"{tag} latte"), TestContext.Current.CancellationToken);

            result.ShouldBe(newer.Value);
        }
    }

    [Fact]
    public async Task Breaks_same_date_ties_by_frequency()
    {
        var tag = $"SCF{Guid.NewGuid():N}";
        var clock = Clock();
        var rare = CategoryId.New(clock);
        var common = CategoryId.New(clock);
        var sameDate = new DateOnly(2026, 4, 10);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(rare, $"{tag} Rare"));
            db.Categories.Add(Category.Create(common, $"{tag} Common"));

            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), sameDate, [Draft($"{tag} kebab", rare)], clock));
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), sameDate, [Draft($"{tag} kebab", common)], clock));
            db.Expenses.Add(Expense.Record(
                ExpenseId.New(clock), sameDate, [Draft($"{tag} kebab", common)], clock));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new SuggestCategoryHandler(db);
            var result = await handler.HandleAsync(new SuggestCategory($"{tag} kebab"), TestContext.Current.CancellationToken);

            result.ShouldBe(common.Value);
        }
    }

    [Fact]
    public async Task Returns_null_when_nothing_matches()
    {
        var clock = Clock();

        await using var db = fixture.CreateContext(clock);
        var handler = new SuggestCategoryHandler(db);
        var result = await handler.HandleAsync(
            new SuggestCategory($"no-such-item-{Guid.NewGuid():N}"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }
}
