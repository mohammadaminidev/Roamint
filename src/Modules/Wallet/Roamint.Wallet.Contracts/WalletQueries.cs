using Roamint.Application.Abstractions;
using Roamint.Wallet.Application;

namespace Roamint.Wallet.Contracts;

/// <summary>Dispatches read queries and maps internal projections to primitive DTOs.</summary>
internal sealed class WalletQueries(IQueryDispatcher queries) : IWalletQueries
{
    public async Task<IReadOnlyList<CurrentRateDto>> GetCurrentRatesAsync(CancellationToken ct = default)
    {
        var rates = await queries.QueryAsync(new GetCurrentRates(), ct);
        return rates
            .Select(rate => new CurrentRateDto(
                rate.BaseCurrencyCode, rate.QuoteCurrencyCode, rate.Rate, rate.ObservedAt, rate.Source))
            .ToList();
    }
}
