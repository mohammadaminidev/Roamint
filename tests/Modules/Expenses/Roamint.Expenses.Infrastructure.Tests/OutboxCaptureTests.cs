using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class OutboxCaptureTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Writes_an_outbox_row_per_domain_event_and_clears_the_buffer()
    {
        var clock = new FakeTimeProvider(Now);
        var categoryId = CategoryId.New(clock);
        var expenseId = ExpenseId.New(clock);

        await using var db = fixture.CreateContext(clock);

        var category = Category.Create(categoryId, "Rent");
        var expense = Expense.Record(
            expenseId, categoryId, Money.Of(800m, Currency.Usdt), new DateOnly(2026, 4, 1), clock);

        db.Categories.Add(category);
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Buffer drained after the save.
        category.DomainEvents.ShouldBeEmpty();
        expense.DomainEvents.ShouldBeEmpty();

        var rows = await RowsForAggregate(db, categoryId.Value, expenseId.Value);
        rows.Select(row => row.Type).ShouldBe(
            ["expenses.category-created", "expenses.expense-recorded"], ignoreOrder: true);
        rows.ShouldAllBe(row => row.ProcessedOnUtc == null);
        rows.ShouldAllBe(row => row.OccurredOnUtc == Now);
        rows.Single(row => row.Type == "expenses.category-created").Payload.ShouldContain("Rent");
    }

    [Fact]
    public async Task Rolls_back_the_aggregate_and_its_outbox_rows_together()
    {
        var clock = new FakeTimeProvider(Now);
        var categoryId = CategoryId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        await using (var tx = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            db.Categories.Add(Category.Create(categoryId, "Discarded"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            (await db.Categories.AnyAsync(c => c.Id == categoryId, TestContext.Current.CancellationToken))
                .ShouldBeFalse();
            (await RowsForAggregate(db, categoryId.Value)).ShouldBeEmpty();
        }
    }

    // The payload is JSON containing the strongly-typed id's Guid, so filtering by id keeps assertions
    // isolated from rows other tests wrote to the shared database.
    private static async Task<List<Outbox.OutboxMessage>> RowsForAggregate(
        ExpensesDbContext db, params Guid[] ids)
    {
        var all = await db.OutboxMessages.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        return all
            .Where(row => ids.Any(id => row.Payload.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
