using Roamint.Application.Abstractions;
using Roamint.Wallet.Application;

namespace Roamint.Wallet.Contracts;

/// <summary>Translates primitive request DTOs into the module's commands and dispatches them.</summary>
internal sealed class WalletApi(ICommandDispatcher commands) : IWalletApi
{
    public async Task<Guid> RecordExchangeAsync(RecordExchangeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = await commands.SendAsync(
            new RecordExchange(
                request.FromAmount,
                request.FromCurrencyCode,
                request.ToAmount,
                request.ToCurrencyCode,
                request.OccurredOn),
            ct);
        return id.Value;
    }

    public Task SetRateAsync(SetRateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return commands.SendAsync(new SetRate(request.QuoteCurrencyCode, request.QuotePerUsd), ct);
    }
}
