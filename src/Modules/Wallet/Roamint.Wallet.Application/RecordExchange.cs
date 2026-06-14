using Roamint.Application.Abstractions;
using Roamint.SharedKernel;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Application;

/// <summary>
/// Records a currency swap — gave <paramref name="FromAmount"/> <paramref name="FromCurrencyCode"/>,
/// received <paramref name="ToAmount"/> <paramref name="ToCurrencyCode"/>. The implied rate becomes the
/// current rate for the pair (latest-wins). Returns the new exchange's id.
/// </summary>
public sealed record RecordExchange(
    decimal FromAmount,
    string FromCurrencyCode,
    decimal ToAmount,
    string ToCurrencyCode,
    DateOnly OccurredOn) : ICommand<ExchangeId>;

internal sealed class RecordExchangeHandler(
    IExchangeRepository exchanges,
    IExchangeRateRepository rates,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RecordExchange, ExchangeId>
{
    public async Task<ExchangeId> HandleAsync(RecordExchange command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var from = Money.Of(command.FromAmount, Currency.FromCode(command.FromCurrencyCode));
        var to = Money.Of(command.ToAmount, Currency.FromCode(command.ToCurrencyCode));

        var id = ExchangeId.New(clock);
        var exchange = Exchange.Record(id, from, to, command.OccurredOn, clock);
        await exchanges.AddAsync(exchange, ct);

        await RateObservation.ObserveAsync(
            rates, from.Currency, to.Currency, exchange.ImpliedRate, clock.GetUtcNow(), RateSource.Exchange, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return id;
    }
}
