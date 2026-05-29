using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Of_rounds_to_currency_minor_units()
    {
        Money.Of(1.005m, Currency.Usd).Amount.ShouldBe(1.01m);
        Money.Of(1.004m, Currency.Usd).Amount.ShouldBe(1.00m);
        Money.Of(1.5m, Currency.Amd).Amount.ShouldBe(2m);
        Money.Of(123.456789m, Currency.Usdt).Amount.ShouldBe(123.46m);
    }

    [Fact]
    public void Addition_requires_matching_currency()
    {
        var usd = Money.Of(10m, Currency.Usd);
        var amd = Money.Of(10m, Currency.Amd);
        Should.Throw<CurrencyMismatchException>(() => usd + amd);
    }

    [Fact]
    public void Subtraction_requires_matching_currency()
    {
        var usd = Money.Of(10m, Currency.Usd);
        var amd = Money.Of(10m, Currency.Amd);
        Should.Throw<CurrencyMismatchException>(() => usd - amd);
    }

    [Fact]
    public void Multiplication_by_quantity_rounds_to_minor_units()
    {
        var unit = Money.Of(1.33m, Currency.Usd);
        (unit * 3m).ShouldBe(Money.Of(3.99m, Currency.Usd));
    }

    [Fact]
    public void Zero_has_zero_amount_in_the_specified_currency()
    {
        var zero = Money.Zero(Currency.Usdt);
        zero.Amount.ShouldBe(0m);
        zero.Currency.ShouldBe(Currency.Usdt);
        zero.IsPositive.ShouldBeFalse();
    }
}
