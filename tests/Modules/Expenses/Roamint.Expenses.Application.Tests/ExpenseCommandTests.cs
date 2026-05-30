using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;
using Shouldly;

namespace Roamint.Expenses.Application.Tests;

public sealed class ExpenseCommandTests
{
    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero));

    private static readonly DateOnly Today = new(2026, 5, 30);

    [Fact]
    public async Task RecordExpense_persists_returns_id_and_saves_once()
    {
        var (expenses, categories, uow) = NewStore();
        var category = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var handler = new RecordExpenseHandler(expenses, categories, uow, Clock);

        var id = await handler.HandleAsync(
            new RecordExpense(category.Id, 1500m, "AMD", Today, "lunch"),
            TestContext.Current.CancellationToken);

        id.Value.ShouldNotBe(Guid.Empty);
        var stored = expenses.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(id);
        stored.CategoryId.ShouldBe(category.Id);
        stored.Amount.ShouldBe(Money.Of(1500m, Currency.Amd));
        stored.Note.ShouldBe("lunch");
        stored.DomainEvents.OfType<ExpenseRecorded>().ShouldHaveSingleItem();
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task RecordExpense_with_matching_lines_succeeds()
    {
        var (expenses, categories, uow) = NewStore();
        var category = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var handler = new RecordExpenseHandler(expenses, categories, uow, Clock);
        var lines = new List<ExpenseLineInput>
        {
            new("Bread", 2m, 30m),
            new("Milk", 1m, 40m),
        };

        var id = await handler.HandleAsync(
            new RecordExpense(category.Id, 100m, "USD", Today, Lines: lines),
            TestContext.Current.CancellationToken);

        var stored = expenses.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(id);
        stored.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RecordExpense_with_line_totals_that_dont_match_throws_domain()
    {
        var (expenses, categories, uow) = NewStore();
        var category = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var handler = new RecordExpenseHandler(expenses, categories, uow, Clock);
        var lines = new List<ExpenseLineInput> { new("Bread", 1m, 50m) };

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense(category.Id, 100m, "USD", Today, Lines: lines),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecordExpense_missing_category_throws_not_found()
    {
        var (expenses, categories, uow) = NewStore();
        var handler = new RecordExpenseHandler(expenses, categories, uow, Clock);

        await Should.ThrowAsync<CategoryNotFoundException>(() => handler.HandleAsync(
            new RecordExpense(CategoryId.New(Clock), 10m, "USD", Today),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecordExpense_archived_category_throws_domain()
    {
        var (expenses, categories, uow) = NewStore();
        var category = Category.Create(CategoryId.New(Clock), "Old");
        category.Archive();
        categories.Seed(category);
        var handler = new RecordExpenseHandler(expenses, categories, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense(category.Id, 10m, "USD", Today),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordExpense_unknown_currency_throws_domain()
    {
        var (expenses, categories, uow) = NewStore();
        var category = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var handler = new RecordExpenseHandler(expenses, categories, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense(category.Id, 10m, "EUR", Today),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecategorizeExpense_missing_expense_throws_not_found()
    {
        var (expenses, categories, uow) = NewStore();
        var handler = new RecategorizeExpenseHandler(expenses, categories, uow);

        await Should.ThrowAsync<ExpenseNotFoundException>(() => handler.HandleAsync(
            new RecategorizeExpense(ExpenseId.New(Clock), CategoryId.New(Clock)),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecategorizeExpense_archived_target_throws_domain()
    {
        var (expenses, categories, uow) = NewStore();
        var from = categories.Seed(Category.Create(CategoryId.New(Clock), "From"));
        var archived = Category.Create(CategoryId.New(Clock), "Archived");
        archived.Archive();
        categories.Seed(archived);
        var expense = expenses.Seed(Expense.Record(
            ExpenseId.New(Clock), from.Id, Money.Of(10m, Currency.Usd), Today, Clock));
        var handler = new RecategorizeExpenseHandler(expenses, categories, uow);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecategorizeExpense(expense.Id, archived.Id),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecategorizeExpense_moves_expense_and_saves()
    {
        var (expenses, categories, uow) = NewStore();
        var from = categories.Seed(Category.Create(CategoryId.New(Clock), "From"));
        var to = categories.Seed(Category.Create(CategoryId.New(Clock), "To"));
        var expense = expenses.Seed(Expense.Record(
            ExpenseId.New(Clock), from.Id, Money.Of(10m, Currency.Usd), Today, Clock));
        var handler = new RecategorizeExpenseHandler(expenses, categories, uow);

        await handler.HandleAsync(new RecategorizeExpense(expense.Id, to.Id), TestContext.Current.CancellationToken);

        expense.CategoryId.ShouldBe(to.Id);
        expense.DomainEvents.OfType<ExpenseRecategorized>().ShouldHaveSingleItem();
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task CorrectExpense_missing_throws_not_found()
    {
        var (expenses, _, uow) = NewStore();
        var handler = new CorrectExpenseHandler(expenses, uow, Clock);

        await Should.ThrowAsync<ExpenseNotFoundException>(() => handler.HandleAsync(
            new CorrectExpense(ExpenseId.New(Clock), 5m, "USD", Today),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorrectExpense_updates_amount_and_saves()
    {
        var (expenses, categories, uow) = NewStore();
        var category = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var expense = expenses.Seed(Expense.Record(
            ExpenseId.New(Clock), category.Id, Money.Of(100m, Currency.Usd), Today, Clock));
        var handler = new CorrectExpenseHandler(expenses, uow, Clock);

        await handler.HandleAsync(
            new CorrectExpense(expense.Id, 200m, "USD", Today, "fixed"),
            TestContext.Current.CancellationToken);

        expense.Amount.ShouldBe(Money.Of(200m, Currency.Usd));
        expense.Note.ShouldBe("fixed");
        expense.DomainEvents.OfType<ExpenseCorrected>().ShouldHaveSingleItem();
        uow.SaveCount.ShouldBe(1);
    }

    private static (InMemoryExpenseRepository, InMemoryCategoryRepository, FakeUnitOfWork) NewStore() =>
        (new InMemoryExpenseRepository(), new InMemoryCategoryRepository(), new FakeUnitOfWork());
}
