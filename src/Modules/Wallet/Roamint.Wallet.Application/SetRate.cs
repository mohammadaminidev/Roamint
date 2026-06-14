using Roamint.Application.Abstractions;
using Roamint.SharedKernel;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Application;

/// <summary>
/// Manually sets a rate, USD-anchored: <paramref name="QuotePerUsd"/> units of
/// <paramref name="QuoteCurrencyCode"/> per one USD. Wins over older observations for the pair.
/// </summary>
public sealed record SetRate(string QuoteCurrencyCode, decimal QuotePerUsd) : ICommand;

internal sealed class SetRateHandler(
    IExchangeRateRepository rates,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetRate>
{
    public async Task HandleAsync(SetRate command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var quote = Currency.FromCode(command.QuoteCurrencyCode);
        await RateObservation.ObserveAsync(
            rates, Currency.Usd, quote, command.QuotePerUsd, clock.GetUtcNow(), RateSource.Manual, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
