using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain.Tests;

public class ExpenseTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-05-29T12:00:00Z", CultureInfo.InvariantCulture));
    private static readonly DateOnly Today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
    private static readonly CategoryId AnyCategory = CategoryId.New(Clock);

    [Fact]
    public void Record_happy_path_raises_ExpenseRecorded()
    {
        var amount = Money.Of(12.50m, Currency.Usd);
        var id = ExpenseId.New(Clock);

        var expense = Expense.Record(id, AnyCategory, amount, Today, Clock, note: "  Lunch  ");

        expense.Amount.ShouldBe(amount);
        expense.OccurredOn.ShouldBe(Today);
        expense.Note.ShouldBe("Lunch");
        expense.Lines.ShouldBeEmpty();
        var ev = expense.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExpenseRecorded>();
        ev.ExpenseId.ShouldBe(id);
        ev.Amount.ShouldBe(amount);
    }

    [Fact]
    public void Record_rejects_non_positive_amount()
    {
        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), AnyCategory, Money.Of(0m, Currency.Usd), Today, Clock));
    }

    [Fact]
    public void Record_rejects_future_date()
    {
        var future = Today.AddDays(1);
        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), AnyCategory, Money.Of(5m, Currency.Usd), future, Clock));
    }

    [Fact]
    public void Record_accepts_matching_line_totals()
    {
        var amount = Money.Of(10m, Currency.Usd);
        var lines = new[]
        {
            new ExpenseLineDraft("Bread", 2m, Money.Of(2m, Currency.Usd)),
            new ExpenseLineDraft("Milk", 3m, Money.Of(2m, Currency.Usd)),
        };

        var expense = Expense.Record(ExpenseId.New(Clock), AnyCategory, amount, Today, Clock, lines: lines);

        expense.Lines.Count.ShouldBe(2);
        expense.Lines.Sum(l => l.LineTotal.Amount).ShouldBe(amount.Amount);
    }

    [Fact]
    public void Record_rejects_mismatched_line_total()
    {
        var amount = Money.Of(10m, Currency.Usd);
        var lines = new[] { new ExpenseLineDraft("Bread", 1m, Money.Of(9m, Currency.Usd)) };

        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), AnyCategory, amount, Today, Clock, lines: lines));
    }

    [Fact]
    public void Record_rejects_line_with_mismatched_currency()
    {
        var amount = Money.Of(10m, Currency.Usd);
        var lines = new[] { new ExpenseLineDraft("Bread", 1m, Money.Of(10m, Currency.Amd)) };

        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), AnyCategory, amount, Today, Clock, lines: lines));
    }

    [Fact]
    public void Recategorize_to_same_category_is_noop()
    {
        var expense = Expense.Record(ExpenseId.New(Clock), AnyCategory, Money.Of(5m, Currency.Usd), Today, Clock);
        expense.ClearDomainEvents();

        expense.Recategorize(AnyCategory);

        expense.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Recategorize_raises_event_with_old_and_new_ids()
    {
        var expense = Expense.Record(ExpenseId.New(Clock), AnyCategory, Money.Of(5m, Currency.Usd), Today, Clock);
        expense.ClearDomainEvents();
        var newCategory = CategoryId.New(Clock);

        expense.Recategorize(newCategory);

        var ev = expense.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExpenseRecategorized>();
        ev.PreviousCategoryId.ShouldBe(AnyCategory);
        ev.NewCategoryId.ShouldBe(newCategory);
    }

    [Fact]
    public void Correct_updates_state_and_raises_event()
    {
        var expense = Expense.Record(ExpenseId.New(Clock), AnyCategory, Money.Of(5m, Currency.Usd), Today, Clock);
        expense.ClearDomainEvents();
        var newAmount = Money.Of(7.50m, Currency.Usd);

        expense.Correct(newAmount, Today, Clock, note: "Corrected");

        expense.Amount.ShouldBe(newAmount);
        expense.Note.ShouldBe("Corrected");
        expense.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExpenseCorrected>()
            .Amount.ShouldBe(newAmount);
    }

    [Fact]
    public void ExpenseLine_rejects_non_positive_quantity()
    {
        var amount = Money.Of(5m, Currency.Usd);
        var bad = new[] { new ExpenseLineDraft("Bread", 0m, Money.Of(5m, Currency.Usd)) };

        Should.Throw<DomainException>(() =>
            Expense.Record(ExpenseId.New(Clock), AnyCategory, amount, Today, Clock, lines: bad));
    }
}
