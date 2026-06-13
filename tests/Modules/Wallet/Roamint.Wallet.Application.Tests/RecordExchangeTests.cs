using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;
using Roamint.Wallet.Application;
using Roamint.Wallet.Domain;
using Shouldly;

namespace Roamint.Wallet.Application.Tests;

public sealed class RecordExchangeTests
{
    private static readonly DateOnly Today = new(2026, 5, 30);

    private static FakeTimeProvider NewClock() =>
        new(new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero));

    private static (RecordExchangeHandler Handler, InMemoryExchangeRepository Exchanges, InMemoryExchangeRateRepository Rates, FakeUnitOfWork Uow)
        NewHandler(TimeProvider clock)
    {
        var exchanges = new InMemoryExchangeRepository();
        var rates = new InMemoryExchangeRateRepository();
        var uow = new FakeUnitOfWork();
        return (new RecordExchangeHandler(exchanges, rates, uow, clock), exchanges, rates, uow);
    }

    [Fact]
    public async Task Records_swap_stamps_rate_and_saves_once()
    {
        var (handler, exchanges, rates, uow) = NewHandler(NewClock());

        var id = await handler.HandleAsync(
            new RecordExchange(100m, "USD", 40_000m, "AMD", Today), TestContext.Current.CancellationToken);

        id.Value.ShouldNotBe(Guid.Empty);
        exchanges.All.ShouldHaveSingleItem().Id.ShouldBe(id);

        var rate = rates.All.ShouldHaveSingleItem();
        rate.Id.ShouldBe(CurrencyPair.Canonical(Currency.Usd, Currency.Amd));
        rate.Rate.ShouldBe(400m);
        rate.Source.ShouldBe(RateSource.Exchange);
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Reversed_direction_swap_stores_canonical_reciprocal()
    {
        var (handler, _, rates, _) = NewHandler(NewClock());

        // Gave AMD, received USD: AMD→USD = 0.0025; canonical USD→AMD must be 400.
        await handler.HandleAsync(
            new RecordExchange(40_000m, "AMD", 100m, "USD", Today), TestContext.Current.CancellationToken);

        var rate = rates.All.ShouldHaveSingleItem();
        rate.Id.ShouldBe(CurrencyPair.Canonical(Currency.Usd, Currency.Amd));
        rate.Rate.ShouldBe(400m);
    }

    [Fact]
    public async Task Later_exchange_for_same_pair_updates_the_rate()
    {
        var clock = NewClock();
        var (handler, _, rates, _) = NewHandler(clock);

        await handler.HandleAsync(new RecordExchange(100m, "USD", 40_000m, "AMD", Today), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(1));
        await handler.HandleAsync(new RecordExchange(100m, "USD", 41_000m, "AMD", Today), TestContext.Current.CancellationToken);

        rates.All.ShouldHaveSingleItem().Rate.ShouldBe(410m);
    }

    [Fact]
    public async Task Rejects_unknown_currency()
    {
        var (handler, _, _, uow) = NewHandler(NewClock());

        await Should.ThrowAsync<DomainException>(() =>
            handler.HandleAsync(new RecordExchange(100m, "GBP", 40_000m, "AMD", Today), TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Rejects_same_currency_swap()
    {
        var (handler, _, _, _) = NewHandler(NewClock());

        await Should.ThrowAsync<DomainException>(() =>
            handler.HandleAsync(new RecordExchange(100m, "USD", 99m, "USD", Today), TestContext.Current.CancellationToken));
    }
}
