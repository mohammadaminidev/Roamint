using Roamint.SharedKernel;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Application;

/// <summary>Applies an observed rate to a pair's <see cref="ExchangeRate"/>, creating it on first sight.
/// Shared by <see cref="RecordExchangeHandler"/> (implied rate from a swap) and <see cref="SetRateHandler"/>
/// (manual override); latest-wins lives in the aggregate.</summary>
internal static class RateObservation
{
    public static async Task ObserveAsync(
        IExchangeRateRepository rates,
        Currency from,
        Currency to,
        decimal rate,
        DateTimeOffset observedAt,
        RateSource source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rates);

        var pair = CurrencyPair.Canonical(from, to);
        var existing = await rates.GetAsync(pair, ct);
        if (existing is null)
        {
            await rates.AddAsync(ExchangeRate.FromObservation(from, to, rate, observedAt, source), ct);
        }
        else
        {
            existing.Observe(from, to, rate, observedAt, source);
        }
    }
}
