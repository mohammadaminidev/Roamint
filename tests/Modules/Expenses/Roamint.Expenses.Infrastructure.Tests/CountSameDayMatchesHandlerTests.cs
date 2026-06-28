using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class CountSameDayMatchesHandlerTests(PostgresFixture fixture)
{
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    // The match key is (date × currency × total) across the whole shared db, so isolate on a date and totals
    // no other test seeds. The amounts below are deliberately unusual to avoid colliding with other fixtures.
    private static readonly DateOnly Day = new(2021, 7, 15);
    private static readonly DateOnly OtherDay = new(2021, 7, 16);

    [Fact]
    public async Task Counts_only_expenses_matching_day_currency_and_total()
    {
        var clock = Clock();
        var foodId = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(foodId, $"Food-{foodId.Value:N}"));

            // Two AMD expenses on Day totalling 4173 — one single-line, one multi-line summing to the same
            // total. Both are twins for (Day, AMD, 4173).
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), Day,
                [new ExpenseLineDraft("Sushi", foodId, Money.Of(4173m, Currency.Amd))], clock));
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), Day,
                [
                    new ExpenseLineDraft("Roll", foodId, Money.Of(4000m, Currency.Amd)),
                    new ExpenseLineDraft("Tea", foodId, Money.Of(173m, Currency.Amd)),
                ], clock));

            // Same day + total but a different currency → not a twin.
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), Day,
                [new ExpenseLineDraft("Imported", foodId, Money.Of(4173m, Currency.Usd))], clock));

            // Same day + currency but a different total → not a twin.
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), Day,
                [new ExpenseLineDraft("Other", foodId, Money.Of(999m, Currency.Amd))], clock));

            // Matching currency + total but on another day → not a twin.
            db.Expenses.Add(Expense.Record(ExpenseId.New(clock), OtherDay,
                [new ExpenseLineDraft("Yesterday", foodId, Money.Of(4173m, Currency.Amd))], clock));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var handler = new CountSameDayMatchesHandler(db);

            var matches = await handler.HandleAsync(
                new CountSameDayMatches(Day, "AMD", 4173m), TestContext.Current.CancellationToken);

            matches.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Returns_zero_when_no_expense_matches()
    {
        var clock = Clock();

        await using var db = fixture.CreateContext(clock);
        var handler = new CountSameDayMatchesHandler(db);

        // No test seeds this date → genuinely empty across the shared db.
        var matches = await handler.HandleAsync(
            new CountSameDayMatches(new DateOnly(1998, 3, 3), "AMD", 4173m),
            TestContext.Current.CancellationToken);

        matches.ShouldBe(0);
    }
}
