using Roamint.Application.Abstractions;
using Roamint.SharedKernel;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Application;

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

        await ObserveRateAsync(
            rates, from.Currency, to.Currency, exchange.ImpliedRate, clock.GetUtcNow(), RateSource.Exchange, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return id;
    }

    /// <summary>Applies an observed rate to the pair's <see cref="ExchangeRate"/>, creating it on first sight.
    /// Shared by the manual override (<see cref="SetRateHandler"/>); latest-wins lives in the aggregate.</summary>
    internal static async Task ObserveRateAsync(
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

internal sealed class SetRateHandler(
    IExchangeRateRepository rates,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetRate>
{
    public async Task HandleAsync(SetRate command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var quote = Currency.FromCode(command.QuoteCurrencyCode);
        await RecordExchangeHandler.ObserveRateAsync(
            rates, Currency.Usd, quote, command.QuotePerUsd, clock.GetUtcNow(), RateSource.Manual, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
