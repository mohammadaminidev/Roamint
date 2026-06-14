using Roamint.SharedKernel;
using Roamint.Wallet.Contracts;

namespace Roamint.Web.Services;

/// <summary>
/// Host-side display conversion (0010 D3/D5). Built from the Wallet module's current rates, it converts a
/// <see cref="Money"/> into a target currency: identity, a direct/reciprocal stored pair, or triangulated
/// through USD. It never guesses — <see cref="TryConvert"/> reports when no path exists so the UI can alert
/// and route to the rate page rather than show a best-effort number. The stored expense is never mutated.
/// </summary>
public sealed class RateConverter
{
    private const string Usd = "USD";

    // Directed rates: (from, to) -> units of `to` per one `from`. Both directions are stored.
    private readonly Dictionary<(string From, string To), decimal> _rates = new();

    public RateConverter(IEnumerable<CurrentRateDto> rates)
    {
        ArgumentNullException.ThrowIfNull(rates);

        foreach (var rate in rates)
        {
            _rates[(rate.BaseCurrencyCode, rate.QuoteCurrencyCode)] = rate.Rate;
            if (rate.Rate != 0m)
            {
                _rates[(rate.QuoteCurrencyCode, rate.BaseCurrencyCode)] = 1m / rate.Rate;
            }
        }
    }

    /// <summary>
    /// Converts <paramref name="amount"/> from <paramref name="from"/> into <paramref name="to"/>, rounded
    /// to the target's minor units. Returns false (with <paramref name="converted"/> = 0) when no direct,
    /// reciprocal, or USD-triangulated path exists.
    /// </summary>
    public bool TryConvert(decimal amount, Currency from, Currency to, out decimal converted)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (from == to)
        {
            converted = Money.Of(amount, to).Amount;
            return true;
        }

        if (_rates.TryGetValue((from.Code, to.Code), out var direct))
        {
            converted = Money.Of(amount * direct, to).Amount;
            return true;
        }

        // Triangulate through USD (the spoke): from -> USD -> to.
        if (_rates.TryGetValue((from.Code, Usd), out var toUsd)
            && _rates.TryGetValue((Usd, to.Code), out var fromUsd))
        {
            converted = Money.Of(amount * toUsd * fromUsd, to).Amount;
            return true;
        }

        converted = 0m;
        return false;
    }

    /// <summary>True when <paramref name="from"/> can be converted into <paramref name="to"/>.</summary>
    public bool CanConvert(Currency from, Currency to) => TryConvert(0m, from, to, out _);
}
