using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class TagPersistenceTests(PostgresFixture fixture)
{
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Round_trips_scoped_and_global_tags_with_readable_status()
    {
        var clock = Clock();
        var groceries = CategoryId.New(clock);
        var scopedId = TagId.New(clock);
        var globalId = TagId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Tags.Add(Tag.Create(scopedId, "Fruit", groceries));
            var archivedGlobal = Tag.Create(globalId, "Retired");
            archivedGlobal.Archive();
            db.Tags.Add(archivedGlobal);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var scoped = await db.Tags.SingleAsync(t => t.Id == scopedId, TestContext.Current.CancellationToken);
            scoped.Scope.ShouldBe(groceries);
            scoped.Status.ShouldBe(TagStatus.Active);

            var global = await db.Tags.SingleAsync(t => t.Id == globalId, TestContext.Current.CancellationToken);
            global.Scope.ShouldBeNull();

            var status = await db.Database
                .SqlQueryRaw<string>("SELECT status AS \"Value\" FROM expenses.tags WHERE id = {0}", globalId.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            status.ShouldBe("Archived");
        }
    }

    [Fact]
    public async Task Persists_line_tags_and_cascades_on_correct()
    {
        var clock = Clock();
        var amd = Currency.Amd;
        var groceries = CategoryId.New(clock);
        var transport = CategoryId.New(clock);
        var fruit = TagId.New(clock);
        var happiness = TagId.New(clock);
        var expenseId = ExpenseId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Categories.Add(Category.Create(groceries, "Groceries"));
            db.Categories.Add(Category.Create(transport, "Transport"));
            db.Tags.Add(Tag.Create(fruit, "Fruit", groceries));
            db.Tags.Add(Tag.Create(happiness, "For Happiness"));
            db.Expenses.Add(Expense.Record(
                expenseId,
                new DateOnly(2026, 4, 30),
                [
                    new ExpenseLineDraft("Apple", groceries, Money.Of(400m, amd), null,
                        new HashSet<TagId> { fruit, happiness }),
                    new ExpenseLineDraft("Taxi", transport, Money.Of(500m, amd)),
                ],
                clock));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var expense = await db.Expenses
                .Include(e => e.Lines)
                .SingleAsync(e => e.Id == expenseId, TestContext.Current.CancellationToken);

            var apple = expense.Lines.Single(line => line.Description == "Apple");
            apple.TagIds.ShouldBe(new HashSet<TagId> { fruit, happiness }, ignoreOrder: true);
            expense.Lines.Single(line => line.Description == "Taxi").TagIds.ShouldBeEmpty();

            var joinRows = await JoinRowCount(db, expenseId);
            joinRows.ShouldBe(2);
        }

        // Correcting to a tag-free line set drops the old lines (and their join rows) by cascade.
        await using (var db = fixture.CreateContext(clock))
        {
            var expense = await db.Expenses
                .Include(e => e.Lines)
                .SingleAsync(e => e.Id == expenseId, TestContext.Current.CancellationToken);
            expense.Correct(new DateOnly(2026, 4, 30), [new ExpenseLineDraft("Bus", transport, Money.Of(300m, amd))], clock);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            (await JoinRowCount(db, expenseId)).ShouldBe(0);
        }
    }

    [Fact]
    public async Task FindActiveByName_is_case_insensitive_scope_sensitive_and_skips_archived()
    {
        var clock = Clock();
        var groceries = CategoryId.New(clock);
        var token = $"FAB{Guid.NewGuid():N}";

        await using (var db = fixture.CreateContext(clock))
        {
            db.Tags.Add(Tag.Create(TagId.New(clock), $"{token} Fruit", groceries));
            db.Tags.Add(Tag.Create(TagId.New(clock), $"{token} Gift")); // global, same token prefix
            var archived = Tag.Create(TagId.New(clock), $"{token} Dead", groceries);
            archived.Archive();
            db.Tags.Add(archived);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var repo = new TagRepository(db);

            // Case-insensitive, correct scope.
            var found = await repo.FindActiveByNameAsync($"{token} fruit", groceries, TestContext.Current.CancellationToken);
            found.ShouldNotBeNull();
            found!.Name.ShouldBe($"{token} Fruit");

            // Same name, wrong scope (global) → no match.
            (await repo.FindActiveByNameAsync($"{token} Fruit", null, TestContext.Current.CancellationToken)).ShouldBeNull();

            // Global match.
            (await repo.FindActiveByNameAsync($"{token} Gift", null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            // Archived is skipped.
            (await repo.FindActiveByNameAsync($"{token} Dead", groceries, TestContext.Current.CancellationToken)).ShouldBeNull();
        }
    }

    private static async Task<int> JoinRowCount(ExpensesDbContext db, ExpenseId expenseId) =>
        await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT COUNT(*)::int AS "Value"
                FROM expenses.expense_line_tags t
                JOIN expenses.expense_lines l ON l.id = t.expense_line_id
                WHERE l.expense_id = {0}
                """,
                expenseId.Value)
            .SingleAsync(TestContext.Current.CancellationToken);
}
