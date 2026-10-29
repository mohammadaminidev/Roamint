using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class GetPayerSummaryHandlerTests(PostgresFixture fixture)
{
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    // A range no other test seeds, plus unique payer keys, keeps the assertions isolated on the shared db.
    private static readonly DateOnly RangeFrom = new(2019, 2, 1);
    private static readonly DateOnly RangeTo = new(2019, 2, 28);

    [Fact]
    public async Task Groups_by_payer_and_currency_summing_every_line_within_the_range()
    {
        var clock = Clock();
        var categoryId = CategoryId.New(clock);
        var suffix = categoryId.Value.ToString("N")[..8];
        var sara = $"sara-{suffix}";
        var omid = $"omid-{suffix}";

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(categoryId, $"Food-{categoryId.Value:N}"));

            // Two-line AMD expense paid by sara → both lines count toward (sara, AMD).
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2019, 2, 3),
                [
                    new ExpenseLineDraft("Bread", categoryId, Money.Of(500m, Currency.Amd)),
                    new ExpenseLineDraft("Cheese", categoryId, Money.Of(2500m, Currency.Amd)),
                ], clock, paidBy: sara));
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2019, 2, 10),
                [new ExpenseLineDraft("Dinner", categoryId, Money.Of(15000m, Currency.Amd))], clock, paidBy: sara));
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2019, 2, 11),
                [new ExpenseLineDraft("Taxi", categoryId, Money.Of(4m, Currency.Usd))], clock, paidBy: omid));

            // Out of range → excluded.
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), new DateOnly(2019, 3, 1),
                [new ExpenseLineDraft("Late", categoryId, Money.Of(999m, Currency.Amd))], clock, paidBy: sara));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var summary = await new GetPayerSummaryHandler(db).HandleAsync(
                new GetPayerSummary(RangeFrom, RangeTo), TestContext.Current.CancellationToken);

            var mine = summary.Where(row => row.PaidBy == sara || row.PaidBy == omid).ToList();
            mine.Count.ShouldBe(2);
            mine.Single(row => row.PaidBy == sara).ShouldBe(new PayerSpendView(sara, "AMD", 18000m));
            mine.Single(row => row.PaidBy == omid).ShouldBe(new PayerSpendView(omid, "USD", 4m));
        }
    }

    [Fact]
    public async Task Unattributed_expenses_group_under_a_null_payer()
    {
        var clock = Clock();
        var categoryId = CategoryId.New(clock);
        var day = new DateOnly(2018, 7, 9); // no other test uses this day

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(categoryId, $"Misc-{categoryId.Value:N}"));
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), day,
                [new ExpenseLineDraft("Stamp", categoryId, Money.Of(7m, Currency.Usdt))], clock));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var summary = await new GetPayerSummaryHandler(db).HandleAsync(
                new GetPayerSummary(day, day), TestContext.Current.CancellationToken);

            summary.ShouldHaveSingleItem().ShouldBe(new PayerSpendView(null, "USDT", 7m));
        }
    }
}
