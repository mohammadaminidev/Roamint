using Microsoft.EntityFrameworkCore;
using Roamint.Application.Abstractions;
using Roamint.Wallet.Application;

namespace Roamint.Wallet.Infrastructure;

/// <summary>
/// Read model for the current-rates list. Reads the (tiny) <c>wallet.exchange_rates</c> table with
/// <c>AsNoTracking</c> and projects in memory — the canonical <see cref="Domain.CurrencyPair"/> key is a
/// converted value type, so its Base/Quote codes are read off the materialized aggregate, not in SQL.
/// </summary>
internal sealed class GetCurrentRatesHandler(WalletDbContext db)
    : IQueryHandler<GetCurrentRates, IReadOnlyList<RateView>>
{
    public async Task<IReadOnlyList<RateView>> HandleAsync(GetCurrentRates query, CancellationToken ct)
    {
        var rates = await db.ExchangeRates.AsNoTracking().ToListAsync(ct);

        return rates
            .OrderBy(rate => rate.Id.Base.Code, StringComparer.Ordinal)
            .ThenBy(rate => rate.Id.Quote.Code, StringComparer.Ordinal)
            .Select(rate => new RateView(
                rate.Id.Base.Code,
                rate.Id.Quote.Code,
                rate.Rate,
                rate.ObservedAt,
                rate.Source.ToString()))
            .ToList();
    }
}
