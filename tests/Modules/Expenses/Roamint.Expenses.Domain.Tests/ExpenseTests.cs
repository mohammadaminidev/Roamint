using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain.Tests;

public class ExpenseTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-05-29T12:00:00Z", CultureInfo.InvariantCulture));
    private static readonly DateOnly Today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
    private static readonly CategoryId Food = CategoryId.New(Clock);
    private static readonly CategoryId Hygiene = CategoryId.New(Clock);

    private static ExpenseLineDraft Line(string description, decimal amount, Currency currency, CategoryId? category = null, Quantity? quantity = null) =>
        new(description, category ?? Food, Money.Of(amount, currency), quantity);

    [Fact]
    public void Record_happy_path_derives_total_and_raises_ExpenseRecorded()
    {
        var id = ExpenseId.New(Clock);
        var lines = new[]
        {
            Line("Apple", 400m, Currency.Amd, Food),
            Line("Shampoo", 1000m, Currency.Amd, Hygiene),
        };

        var expense = Expense.Record(id, Today, lines, Clock, note: "  Groceries  ");

        expense.OccurredOn.ShouldBe(Today);
        expense.Note.ShouldBe("Groceries");
        expense.Currency.ShouldBe(Currency.Amd);
        expense.Total.ShouldBe(Money.Of(1400m, Currency.Amd));
        expense.Lines.Count.ShouldBe(2);

        var ev = expense.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExpenseRecorded>();
        ev.ExpenseId.ShouldBe(id);
        ev.Total.ShouldBe(Money.Of(1400m, Currency.Amd));
        ev.OccurredOn.ShouldBe(Today);
    }

    [Fact]
    public void Record_keeps_per_line_category()
    {
        var lines = new[]
        {
            Line("Apple", 400m, Currency.Amd, Food),
            Line("Shampoo", 1000m, Currency.Amd, Hygiene),
        };

        var expense = Expense.Record(ExpenseId.New(Clock), Today, lines, Clock);

        expense.Lines[0].CategoryId.ShouldBe(Food);
        expense.Lines[1].CategoryId.ShouldBe(Hygiene);
    }

    [Fact]
    public void Record_rejects_empty_lines()
    {
        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), Today, [], Clock));
    }

    [Fact]
    public void Record_rejects_future_date()
    {
        var future = Today.AddDays(1);
        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), future, [Line("Apple", 5m, Currency.Usd)], Clock));
    }

    [Fact]
    public void Record_rejects_line_with_non_positive_amount()
    {
        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), Today, [Line("Apple", 0m, Currency.Usd)], Clock));
    }

    [Fact]
    public void Record_rejects_lines_with_mixed_currencies()
    {
        var lines = new[]
        {
            Line("Apple", 5m, Currency.Usd),
            Line("Shampoo", 1000m, Currency.Amd),
        };

        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), Today, lines, Clock));
    }

    [Fact]
    public void Record_rejects_blank_line_description()
    {
        // Null/blank is a guard-clause ArgumentException; the length rule is the DomainException.
        Should.Throw<ArgumentException>(() =>
            Expense.Record(ExpenseId.New(Clock), Today, [Line("   ", 5m, Currency.Usd)], Clock));
    }

    [Fact]
    public void Record_carries_optional_quantity()
    {
        var lines = new[]
        {
            Line("Apple", 400m, Currency.Amd, Food, Quantity.Of(500m, QuantityUnit.Grams)),
            Line("Knife", 900m, Currency.Amd, Food),
        };

        var expense = Expense.Record(ExpenseId.New(Clock), Today, lines, Clock);

        expense.Lines[0].Quantity.ShouldBe(Quantity.Of(500m, QuantityUnit.Grams));
        expense.Lines[1].Quantity.ShouldBeNull();
    }

    [Fact]
    public void Record_carries_optional_line_tags()
    {
        var fruit = TagId.New(Clock);
        var happiness = TagId.New(Clock);
        var lines = new[]
        {
            new ExpenseLineDraft("Apple", Food, Money.Of(400m, Currency.Amd), TagIds: new HashSet<TagId> { fruit, happiness }),
            new ExpenseLineDraft("Knife", Food, Money.Of(900m, Currency.Amd)),
        };

        var expense = Expense.Record(ExpenseId.New(Clock), Today, lines, Clock);

        expense.Lines[0].TagIds.ShouldBe(new HashSet<TagId> { fruit, happiness }, ignoreOrder: true);
        expense.Lines[1].TagIds.ShouldBeEmpty();
    }

    [Fact]
    public void Record_trims_blank_note_to_null()
    {
        var expense = Expense.Record(ExpenseId.New(Clock), Today, [Line("Apple", 5m, Currency.Usd)], Clock, note: "   ");

        expense.Note.ShouldBeNull();
    }

    [Fact]
    public void Correct_replaces_lines_and_raises_event_with_new_total()
    {
        var expense = Expense.Record(ExpenseId.New(Clock), Today, [Line("Apple", 5m, Currency.Usd)], Clock);
        expense.ClearDomainEvents();

        expense.Correct(Today, [Line("Bread", 3m, Currency.Usd), Line("Milk", 4m, Currency.Usd)], Clock, note: "Corrected");

        expense.Total.ShouldBe(Money.Of(7m, Currency.Usd));
        expense.Lines.Count.ShouldBe(2);
        expense.Note.ShouldBe("Corrected");
        expense.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExpenseCorrected>()
            .Total.ShouldBe(Money.Of(7m, Currency.Usd));
    }

    [Fact]
    public void Correct_rejects_empty_lines()
    {
        var expense = Expense.Record(ExpenseId.New(Clock), Today, [Line("Apple", 5m, Currency.Usd)], Clock);

        Should.Throw<DomainException>(() => expense.Correct(Today, [], Clock));
    }
}
