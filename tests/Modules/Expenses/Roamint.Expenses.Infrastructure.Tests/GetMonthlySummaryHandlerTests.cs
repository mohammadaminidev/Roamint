using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class GetMonthlySummaryHandlerTests(PostgresFixture fixture)
{
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    // Like ListExpensesHandlerTests, seed in 2020 (past vs. the clock) and isolate by minting unique
    // category ids — the summary aggregates across every expense in the range on the shared db, so all
    // assertions filter to the seeded categories.
    private static readonly DateOnly RangeFrom = new(2020, 4, 1);
    private static readonly DateOnly RangeTo = new(2020, 4, 30);

    [Fact]
    public async Task Groups_by_line_category_and_currency_summing_line_amounts_within_the_range()
    {
        var clock = Clock();
        var foodId = CategoryId.New(clock);
        var hygieneId = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(foodId, $"Food-{foodId.Value:N}"));
            db.Categories.Add(Category.Create(hygieneId, $"Hygiene-{hygieneId.Value:N}"));

            // Two USD Food lines on different days → one (Food, USD) bucket summing to 20.
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2020, 4, 1),
                [new ExpenseLineDraft("Lunch", foodId, Money.Of(12m, Currency.Usd))], clock));
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2020, 4, 5),
                [new ExpenseLineDraft("Snack", foodId, Money.Of(8m, Currency.Usd))], clock));

            // Mixed-category AMD expense → splits into (Food, AMD)=400 and (Hygiene, AMD)=600.
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2020, 4, 3),
                [
                    new ExpenseLineDraft("Apple", foodId, Money.Of(400m, Currency.Amd)),
                    new ExpenseLineDraft("Soap", hygieneId, Money.Of(600m, Currency.Amd)),
                ], clock));

            // USDT Hygiene line → its own (Hygiene, USDT)=5 bucket.
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2020, 4, 2),
                [new ExpenseLineDraft("Shampoo", hygieneId, Money.Of(5m, Currency.Usdt))], clock));

            // Out of range (March) → must not contribute to the April summary.
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2020, 3, 31),
                [new ExpenseLineDraft("Old lunch", foodId, Money.Of(99m, Currency.Usd))], clock));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new GetMonthlySummaryHandler(db);
            var summary = await handler.HandleAsync(
                new GetMonthlySummary(RangeFrom, RangeTo), TestContext.Current.CancellationToken);

            var mine = summary
                .Where(row => row.CategoryId == foodId.Value || row.CategoryId == hygieneId.Value)
                .ToList();

            mine.Count.ShouldBe(4);

            var foodUsd = mine.Single(row => row.CategoryId == foodId.Value && row.CurrencyCode == "USD");
            foodUsd.Total.ShouldBe(20m);
            foodUsd.CategoryName.ShouldBe($"Food-{foodId.Value:N}");

            mine.Single(row => row.CategoryId == foodId.Value && row.CurrencyCode == "AMD").Total.ShouldBe(400m);
            mine.Single(row => row.CategoryId == hygieneId.Value && row.CurrencyCode == "AMD").Total.ShouldBe(600m);

            var hygieneUsdt = mine.Single(row => row.CategoryId == hygieneId.Value && row.CurrencyCode == "USDT");
            hygieneUsdt.Total.ShouldBe(5m);
            hygieneUsdt.CategoryName.ShouldBe($"Hygiene-{hygieneId.Value:N}");

            // The March expense is excluded: no (Food, USD) bucket carries its 99.
            mine.Where(row => row.CategoryId == foodId.Value && row.CurrencyCode == "USD").Count().ShouldBe(1);
        }
    }

    [Fact]
    public async Task Empty_range_returns_no_rows()
    {
        var clock = Clock();

        await using var db = fixture.CreateContext(clock);
        var handler = new GetMonthlySummaryHandler(db);

        // No test seeds 1999 → the range is genuinely empty across the shared db.
        var summary = await handler.HandleAsync(
            new GetMonthlySummary(new DateOnly(1999, 1, 1), new DateOnly(1999, 1, 31)),
            TestContext.Current.CancellationToken);

        summary.ShouldBeEmpty();
    }
}
