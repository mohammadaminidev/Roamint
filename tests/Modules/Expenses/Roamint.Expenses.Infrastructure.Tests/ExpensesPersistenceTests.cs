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

    private static ExpenseLineDraft Draft(string description, CategoryId category, decimal amount, Currency currency,
        Quantity? quantity = null) =>
        new(description, category, Money.Of(amount, currency), quantity);

    [Fact]
    public async Task Records_and_reads_back_lines_with_categories_amounts_and_quantity()
    {
        var clock = Clock();
        var amd = Currency.Amd;
        var food = CategoryId.New(clock);
        var hygiene = CategoryId.New(clock);
        var expenseId = ExpenseId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(food, "Food"));
            db.Categories.Add(Category.Create(hygiene, "Hygiene"));
            db.Expenses.Add(Expense.Record(
                expenseId,
                new DateOnly(2026, 4, 30),
                [
                    Draft("Apple", food, 400m, amd, Quantity.Of(500m, QuantityUnit.Grams)),
                    Draft("Shampoo", hygiene, 1000m, amd),
                ],
                clock,
                note: "weekly shop"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var expense = await db.Expenses
                .Include(e => e.Lines)
                .SingleAsync(e => e.Id == expenseId, TestContext.Current.CancellationToken);

            expense.Currency.ShouldBe(amd);
            expense.Total.ShouldBe(Money.Of(1400m, amd));
            expense.OccurredOn.ShouldBe(new DateOnly(2026, 4, 30));
            expense.Note.ShouldBe("weekly shop");
            expense.Lines.Count.ShouldBe(2);

            var apple = expense.Lines.Single(line => line.Description == "Apple");
            apple.CategoryId.ShouldBe(food);
            apple.Amount.ShouldBe(Money.Of(400m, amd));
            apple.Quantity.ShouldBe(Quantity.Of(500m, QuantityUnit.Grams));

            var shampoo = expense.Lines.Single(line => line.Description == "Shampoo");
            shampoo.CategoryId.ShouldBe(hygiene);
            shampoo.Quantity.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Round_trips_amd_amount_with_zero_minor_units()
    {
        var clock = Clock();
        var amd = Currency.Amd;
        var taxi = CategoryId.New(clock);
        var expenseId = ExpenseId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(taxi, "Taxi"));
            db.Expenses.Add(Expense.Record(
                expenseId, new DateOnly(2026, 4, 29), [Draft("Taxi", taxi, 3500m, amd)], clock));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var expense = await db.Expenses
                .Include(e => e.Lines)
                .SingleAsync(e => e.Id == expenseId, TestContext.Current.CancellationToken);
            expense.Total.ShouldBe(Money.Of(3500m, amd));
            expense.Currency.ShouldBe(amd);
            expense.Lines.ShouldHaveSingleItem().Amount.ShouldBe(Money.Of(3500m, amd));
        }
    }

    [Fact]
    public async Task Persists_quantity_as_readable_value_and_code()
    {
        var clock = Clock();
        var usd = Currency.Usd;
        var category = CategoryId.New(clock);
        var expenseId = ExpenseId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(category, "Food"));
            db.Expenses.Add(Expense.Record(
                expenseId, new DateOnly(2026, 4, 28),
                [Draft("Cheese", category, 7m, usd, Quantity.Of(250m, QuantityUnit.Grams))], clock));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var unit = await db.Database
                .SqlQueryRaw<string>(
                    "SELECT l.quantity_unit AS \"Value\" FROM expenses.expense_lines l WHERE l.expense_id = {0}",
                    expenseId.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            unit.ShouldBe("g");

            var value = await db.Database
                .SqlQueryRaw<decimal>(
                    "SELECT l.quantity_value AS \"Value\" FROM expenses.expense_lines l WHERE l.expense_id = {0}",
                    expenseId.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            value.ShouldBe(250m);
        }
    }

    [Fact]
    public async Task Correcting_replaces_line_rows_without_orphans()
    {
        var clock = Clock();
        var usd = Currency.Usd;
        var category = CategoryId.New(clock);
        var expenseId = ExpenseId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(category, "Food"));
            db.Expenses.Add(Expense.Record(
                expenseId, new DateOnly(2026, 4, 27),
                [Draft("Bread", category, 3m, usd), Draft("Milk", category, 4m, usd)], clock));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var expense = await db.Expenses
                .Include(e => e.Lines)
                .SingleAsync(e => e.Id == expenseId, TestContext.Current.CancellationToken);
            expense.Correct(new DateOnly(2026, 4, 27), [Draft("Eggs", category, 5m, usd)], clock);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var count = await db.Database
                .SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM expenses.expense_lines WHERE expense_id = {0}",
                    expenseId.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            count.ShouldBe(1);
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
            var category = await db.Categories.SingleAsync(c => c.Id == categoryId, TestContext.Current.CancellationToken);
            category.Status.ShouldBe(CategoryStatus.Archived);

            var stored = await db.Database
                .SqlQueryRaw<string>(
                    "SELECT status AS \"Value\" FROM expenses.categories WHERE id = {0}",
                    categoryId.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            stored.ShouldBe("Archived");
        }
    }
}
