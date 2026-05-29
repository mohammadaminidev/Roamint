using System.Globalization;

namespace Roamint.SharedKernel;

/// <summary>
/// A monetary amount in a single <see cref="Currency"/>. Construct via <see cref="Of"/> so the amount is
/// rounded to the currency's minor units; arithmetic across currencies throws.
/// </summary>
public readonly struct Money : IEquatable<Money>
{
    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsPositive => Amount > 0m;

    /// <summary>Creates a value rounded to the currency's minor units, rejecting non-finite amounts.</summary>
    public static Money Of(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        // decimal cannot be NaN/Infinity, so only the rounding contract needs enforcing here.
        var rounded = Math.Round(amount, currency.MinorUnits, MidpointRounding.AwayFromZero);
        return new Money(rounded, currency);
    }

    public static Money Zero(Currency currency) => Of(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return Of(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return Of(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, decimal factor) => Of(money.Amount * factor, money.Currency);

    public static Money operator *(decimal factor, Money money) => money * factor;

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        }
    }

    public bool Equals(Money other) => Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) => obj is Money money && Equals(money);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public override string ToString() =>
        $"{Amount.ToString("0.############", CultureInfo.InvariantCulture)} {Currency.Code}";

    public static bool operator ==(Money left, Money right) => left.Equals(right);

    public static bool operator !=(Money left, Money right) => !left.Equals(right);
}
