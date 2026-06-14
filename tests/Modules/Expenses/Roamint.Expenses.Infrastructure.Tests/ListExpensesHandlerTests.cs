using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class ListExpensesHandlerTests(PostgresFixture fixture)
{
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    // Other tests use 2026 dates; this test lives entirely in 2020 (still past vs. the clock) so a 2020
    // date-range query isolates its rows on the shared database. Assertions also filter to the seeded ids.
    private static readonly DateOnly RangeFrom = new(2020, 3, 1);
    private static readonly DateOnly RangeTo = new(2020, 3, 31);

    [Fact]
    public async Task Lists_newest_first_with_joined_category_names_and_derived_totals()
    {
        var clock = Clock();
        var marker = Guid.NewGuid().ToString("N");
        var foodId = CategoryId.New(clock);
        var hygieneId = CategoryId.New(clock);
        ExpenseId e1, e2, e3;

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(foodId, $"Food-{marker}"));
            db.Categories.Add(Category.Create(hygieneId, $"Hygiene-{marker}"));

            // E1 2020-03-01 USD, one Food line; E2 2020-03-03 AMD, mixed (Food + Hygiene); E3 2020-03-02 USDT.
            e1 = ExpenseId.New(clock);
            db.Expenses.Add(Expense.Record(e1, new DateOnly(2020, 3, 1),
                [new ExpenseLineDraft("Lunch", foodId, Money.Of(12m, Currency.Usd),
                    Quantity.Of(2m, QuantityUnit.Pieces))], clock));

            e2 = ExpenseId.New(clock);
            db.Expenses.Add(Expense.Record(e2, new DateOnly(2020, 3, 3),
                [
                    new ExpenseLineDraft("Apple", foodId, Money.Of(400m, Currency.Amd)),
                    new ExpenseLineDraft("Soap", hygieneId, Money.Of(600m, Currency.Amd)),
                ], clock, note: "shop"));

            e3 = ExpenseId.New(clock);
            db.Expenses.Add(Expense.Record(e3, new DateOnly(2020, 3, 2),
                [new ExpenseLineDraft("Shampoo", hygieneId, Money.Of(5m, Currency.Usdt))], clock));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new ListExpensesHandler(db);
            var all = await handler.HandleAsync(
                new ListExpenses(RangeFrom, RangeTo, null), TestContext.Current.CancellationToken);

            var mine = all.Where(row => row.Id == e1.Value || row.Id == e2.Value || row.Id == e3.Value).ToList();

            // Newest-first: E2 (03-03), E3 (03-02), E1 (03-01).
            mine.Select(row => row.Id).ShouldBe([e2.Value, e3.Value, e1.Value]);

            var first = mine[0];
            first.CurrencyCode.ShouldBe("AMD");
            first.Total.ShouldBe(1000m);
            first.Note.ShouldBe("shop");
            first.Lines.Count.ShouldBe(2);
            first.Lines.Single(line => line.Description == "Apple").CategoryName.ShouldBe($"Food-{marker}");

            var lunch = mine.Single(row => row.Id == e1.Value).Lines.ShouldHaveSingleItem();
            lunch.CategoryName.ShouldBe($"Food-{marker}");
            lunch.QuantityValue.ShouldBe(2m);
            lunch.QuantityUnit.ShouldBe("Pcs");
        }
    }

    [Fact]
    public async Task Category_filter_matches_an_expense_when_any_line_is_in_the_category()
    {
        var clock = Clock();
        var marker = Guid.NewGuid().ToString("N");
        var foodId = CategoryId.New(clock);
        var hygieneId = CategoryId.New(clock);
        ExpenseId food, mixed, hygiene;

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(foodId, $"Food-{marker}"));
            db.Categories.Add(Category.Create(hygieneId, $"Hygiene-{marker}"));

            food = ExpenseId.New(clock);
            db.Expenses.Add(Expense.Record(food, new DateOnly(2020, 3, 1),
                [new ExpenseLineDraft("Lunch", foodId, Money.Of(12m, Currency.Usd))], clock));

            mixed = ExpenseId.New(clock);
            db.Expenses.Add(Expense.Record(mixed, new DateOnly(2020, 3, 2),
                [
                    new ExpenseLineDraft("Apple", foodId, Money.Of(400m, Currency.Amd)),
                    new ExpenseLineDraft("Soap", hygieneId, Money.Of(600m, Currency.Amd)),
                ], clock));

            hygiene = ExpenseId.New(clock);
            db.Expenses.Add(Expense.Record(hygiene, new DateOnly(2020, 3, 3),
                [new ExpenseLineDraft("Shampoo", hygieneId, Money.Of(5m, Currency.Usdt))], clock));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new ListExpensesHandler(db);
            var rows = await handler.HandleAsync(
                new ListExpenses(RangeFrom, RangeTo, foodId.Value), TestContext.Current.CancellationToken);

            var mineIds = rows
                .Where(row => row.Id == food.Value || row.Id == mixed.Value || row.Id == hygiene.Value)
                .Select(row => row.Id)
                .ToList();

            // The mixed expense matches (its Food line); the hygiene-only one does not.
            mineIds.ShouldBe([mixed.Value, food.Value]);
        }
    }
}
