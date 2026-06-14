using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application;
using Roamint.Expenses.Application.Expenses;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;
using Shouldly;

namespace Roamint.Expenses.Application.Tests;

public sealed class ExpenseCommandTests
{
    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero));

    private static readonly DateOnly Today = new(2026, 5, 30);

    private static ExpenseLineInput Line(string description, CategoryId category, decimal amount,
        decimal? qtyValue = null, QuantityUnit? qtyUnit = null) =>
        new(description, category, amount, qtyValue, qtyUnit);

    private static ExpenseLineDraft Draft(CategoryId category, decimal amount, Currency currency) =>
        new("Item", category, Money.Of(amount, currency));

    [Fact]
    public async Task RecordExpense_persists_returns_id_and_saves_once()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        var id = await handler.HandleAsync(
            new RecordExpense("AMD", Today, [Line("Lunch", food.Id, 1500m)], "lunch"),
            TestContext.Current.CancellationToken);

        id.Value.ShouldNotBe(Guid.Empty);
        var stored = expenses.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(id);
        stored.Currency.ShouldBe(Currency.Amd);
        stored.Total.ShouldBe(Money.Of(1500m, Currency.Amd));
        stored.Note.ShouldBe("lunch");
        stored.Lines.ShouldHaveSingleItem().CategoryId.ShouldBe(food.Id);
        stored.DomainEvents.OfType<ExpenseRecorded>().ShouldHaveSingleItem();
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task RecordExpense_keeps_per_line_categories_and_quantity()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var hygiene = categories.Seed(Category.Create(CategoryId.New(Clock), "Hygiene"));
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);
        var lines = new List<ExpenseLineInput>
        {
            Line("Apple", food.Id, 400m, 500m, QuantityUnit.Grams),
            Line("Shampoo", hygiene.Id, 1000m),
        };

        var id = await handler.HandleAsync(
            new RecordExpense("AMD", Today, lines), TestContext.Current.CancellationToken);

        var stored = expenses.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(id);
        stored.Total.ShouldBe(Money.Of(1400m, Currency.Amd));
        stored.Lines[0].CategoryId.ShouldBe(food.Id);
        stored.Lines[0].Quantity.ShouldBe(Quantity.Of(500m, QuantityUnit.Grams));
        stored.Lines[1].CategoryId.ShouldBe(hygiene.Id);
        stored.Lines[1].Quantity.ShouldBeNull();
    }

    [Fact]
    public async Task RecordExpense_with_no_lines_throws_domain()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense("USD", Today, []),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecordExpense_quantity_with_value_but_no_unit_throws_domain()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense("USD", Today, [Line("Apple", food.Id, 5m, qtyValue: 500m)]),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecordExpense_missing_category_throws_not_found()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<CategoryNotFoundException>(() => handler.HandleAsync(
            new RecordExpense("USD", Today, [Line("Apple", CategoryId.New(Clock), 10m)]),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecordExpense_archived_category_throws_domain()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var archived = Category.Create(CategoryId.New(Clock), "Old");
        archived.Archive();
        categories.Seed(archived);
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense("USD", Today, [Line("Apple", archived.Id, 10m)]),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecordExpense_unknown_currency_throws_domain()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense("EUR", Today, [Line("Apple", food.Id, 10m)]),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorrectExpense_missing_throws_not_found()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var handler = new CorrectExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<ExpenseNotFoundException>(() => handler.HandleAsync(
            new CorrectExpense(ExpenseId.New(Clock), "USD", Today, [Line("Apple", CategoryId.New(Clock), 5m)]),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorrectExpense_replaces_lines_and_saves()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var expense = expenses.Seed(Expense.Record(
            ExpenseId.New(Clock), Today, [Draft(food.Id, 100m, Currency.Usd)], Clock));
        var handler = new CorrectExpenseHandler(expenses, categories, tags, uow, Clock);

        await handler.HandleAsync(
            new CorrectExpense(expense.Id, "USD", Today, [Line("Bread", food.Id, 120m), Line("Milk", food.Id, 80m)], "fixed"),
            TestContext.Current.CancellationToken);

        expense.Total.ShouldBe(Money.Of(200m, Currency.Usd));
        expense.Lines.Count.ShouldBe(2);
        expense.Note.ShouldBe("fixed");
        expense.DomainEvents.OfType<ExpenseCorrected>().ShouldHaveSingleItem();
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task CorrectExpense_archived_category_throws_domain()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var archived = Category.Create(CategoryId.New(Clock), "Old");
        archived.Archive();
        categories.Seed(archived);
        var expense = expenses.Seed(Expense.Record(
            ExpenseId.New(Clock), Today, [Draft(food.Id, 100m, Currency.Usd)], Clock));
        var handler = new CorrectExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new CorrectExpense(expense.Id, "USD", Today, [Line("Apple", archived.Id, 50m)]),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    private static (InMemoryExpenseRepository, InMemoryCategoryRepository, InMemoryTagRepository, FakeUnitOfWork) NewStore() =>
        (new InMemoryExpenseRepository(), new InMemoryCategoryRepository(), new InMemoryTagRepository(), new FakeUnitOfWork());
}
