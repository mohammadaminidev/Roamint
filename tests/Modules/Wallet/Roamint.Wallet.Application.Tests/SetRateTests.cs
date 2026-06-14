using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;
using Roamint.Wallet.Application;
using Roamint.Wallet.Domain;
using Shouldly;

namespace Roamint.Wallet.Application.Tests;

public sealed class SetRateTests
{
    private static readonly DateOnly Today = new(2026, 5, 30);

    private static FakeTimeProvider NewClock() =>
        new(new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Sets_a_manual_usd_anchored_rate_and_saves()
    {
        var rates = new InMemoryExchangeRateRepository();
        var uow = new FakeUnitOfWork();
        var handler = new SetRateHandler(rates, uow, NewClock());

        await handler.HandleAsync(new SetRate("AMD", 405m), TestContext.Current.CancellationToken);

        var rate = rates.All.ShouldHaveSingleItem();
        rate.Id.ShouldBe(CurrencyPair.Canonical(Currency.Usd, Currency.Amd));
        rate.Rate.ShouldBe(405m);
        rate.Source.ShouldBe(RateSource.Manual);
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Sets_a_usd_irt_toman_rate_with_irt_as_quote()
    {
        var rates = new InMemoryExchangeRateRepository();
        var uow = new FakeUnitOfWork();
        var handler = new SetRateHandler(rates, uow, NewClock());

        await handler.HandleAsync(new SetRate("IRT", 91_000m), TestContext.Current.CancellationToken);

        var rate = rates.All.ShouldHaveSingleItem();
        rate.Id.ShouldBe(CurrencyPair.Canonical(Currency.Usd, Currency.Irt));
        rate.Id.ToString().ShouldBe("USD/IRT");
        rate.Rate.ShouldBe(91_000m);
        rate.Source.ShouldBe(RateSource.Manual);
    }

    [Fact]
    public async Task Manual_override_wins_over_an_earlier_exchange_rate()
    {
        var clock = NewClock();
        var rates = new InMemoryExchangeRateRepository();
        var uow = new FakeUnitOfWork();

        var record = new RecordExchangeHandler(new InMemoryExchangeRepository(), rates, uow, clock);
        await record.HandleAsync(new RecordExchange(100m, "USD", 40_000m, "AMD", Today), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromMinutes(5));
        var setRate = new SetRateHandler(rates, uow, clock);
        await setRate.HandleAsync(new SetRate("AMD", 420m), TestContext.Current.CancellationToken);

        var rate = rates.All.ShouldHaveSingleItem();
        rate.Rate.ShouldBe(420m);
        rate.Source.ShouldBe(RateSource.Manual);
    }

    [Fact]
    public async Task Rejects_non_positive_rate()
    {
        var handler = new SetRateHandler(new InMemoryExchangeRateRepository(), new FakeUnitOfWork(), NewClock());

        await Should.ThrowAsync<DomainException>(() =>
            handler.HandleAsync(new SetRate("AMD", 0m), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_usd_as_its_own_quote()
    {
        var handler = new SetRateHandler(new InMemoryExchangeRateRepository(), new FakeUnitOfWork(), NewClock());

        await Should.ThrowAsync<DomainException>(() =>
            handler.HandleAsync(new SetRate("USD", 1m), TestContext.Current.CancellationToken));
    }
}
