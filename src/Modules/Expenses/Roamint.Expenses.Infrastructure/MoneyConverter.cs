using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// Single-column <see cref="Money"/> mapping (<c>"amount|CODE"</c>) for places that never query the amount
/// or currency separately — i.e. the owned <c>ExpenseLine.UnitPrice</c>, where EF's <c>ComplexProperty</c>
/// is unavailable on owned-collection builders. The aggregate-root <c>Expense.Amount</c> uses a two-column
/// complex mapping instead, because the monthly-summary reads group and sum by currency.
/// </summary>
internal sealed class MoneyConverter : ValueConverter<Money, string>
{
    public MoneyConverter()
        : base(money => Format(money), value => Parse(value))
    {
    }

    private static string Format(Money money) =>
        money.Amount.ToString(CultureInfo.InvariantCulture) + "|" + money.Currency.Code;

    private static Money Parse(string value)
    {
        var separator = value.IndexOf('|', StringComparison.Ordinal);
        var amount = decimal.Parse(value[..separator], CultureInfo.InvariantCulture);
        var currency = Currency.FromCode(value[(separator + 1)..]);
        return Money.Of(amount, currency);
    }
}
