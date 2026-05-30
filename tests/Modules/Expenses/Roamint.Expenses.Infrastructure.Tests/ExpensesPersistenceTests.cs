using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class ExpensesPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock() => new(Now);

    [Fact]
    public async Task Records_and_reads_back_an_expense_with_its_lines()
    {
        var clock = Clock();
        var usd = Currency.Usd;
        var categoryId = CategoryId.New(clock);
        var expenseId = ExpenseId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(categoryId, "Groceries"));
            db.Expenses.Add(Expense.Record(
                expenseId,
                categoryId,
                Money.Of(10.00m, usd),
                new DateOnly(2026, 4, 30),
                clock,
                note: "weekly shop",
                lines:
                [
                    new ExpenseLineDraft("Milk", 2m, Money.Of(2.50m, usd)),
                    new ExpenseLineDraft("Bread", 1m, Money.Of(5.00m, usd)),
                ]));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var expense = await db.Expenses.SingleAsync(e => e.Id == expenseId, TestContext.Current.CancellationToken);

            expense.Amount.ShouldBe(Money.Of(10.00m, usd));
            expense.Amount.Currency.ShouldBe(Currency.Usd);
            expense.CategoryId.ShouldBe(categoryId);
            expense.OccurredOn.ShouldBe(new DateOnly(2026, 4, 30));
            expense.Note.ShouldBe("weekly shop");

            expense.Lines.Count.ShouldBe(2);
            expense.Lines.Sum(line => line.LineTotal.Amount).ShouldBe(10.00m);
            expense.Lines.ShouldContain(line =>
                line.Description == "Milk" && line.Quantity == 2m && line.UnitPrice == Money.Of(2.50m, usd));
        }
    }

    [Fact]
    public async Task Round_trips_amd_amount_with_zero_minor_units()
    {
        var clock = Clock();
        var amd = Currency.Amd;
        var categoryId = CategoryId.New(clock);
        var expenseId = ExpenseId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(categoryId, "Taxi"));
            db.Expenses.Add(Expense.Record(
                expenseId, categoryId, Money.Of(3500m, amd), new DateOnly(2026, 4, 29), clock));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var expense = await db.Expenses.SingleAsync(e => e.Id == expenseId, TestContext.Current.CancellationToken);
            expense.Amount.ShouldBe(Money.Of(3500m, amd));
            expense.Amount.Currency.ShouldBe(Currency.Amd);
            expense.Lines.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Persists_category_status_as_a_readable_string()
    {
        var clock = Clock();
        var categoryId = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            var category = Category.Create(categoryId, "Subscriptions");
            category.Archive();
            db.Categories.Add(category);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            // Entity round-trips as the enum...
            var category = await db.Categories.SingleAsync(c => c.Id == categoryId, TestContext.Current.CancellationToken);
            category.Status.ShouldBe(CategoryStatus.Archived);

            // ...and the underlying column is the readable string, not an int.
            var stored = await db.Database
                .SqlQueryRaw<string>(
                    "SELECT status AS \"Value\" FROM expenses.categories WHERE id = {0}",
                    categoryId.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            stored.ShouldBe("Archived");
        }
    }
}
