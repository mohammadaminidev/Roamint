using System.Globalization;
using Roamint.SharedKernel;

namespace Roamint.Wallet.Domain.Tests;

public class ExchangeRateTests
{
    private static readonly DateTimeOffset T1 = DateTimeOffset.Parse("2026-05-29T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset T2 = DateTimeOffset.Parse("2026-05-29T12:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void FromObservation_in_canonical_direction_stores_rate_and_raises_event()
    {
        var rate = ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, T1, RateSource.Exchange);

        rate.Id.ShouldBe(CurrencyPair.Canonical(Currency.Usd, Currency.Amd));
        rate.Rate.ShouldBe(400m);
        rate.ObservedAt.ShouldBe(T1);
        rate.Source.ShouldBe(RateSource.Exchange);

        var ev = rate.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExchangeRateChanged>();
        ev.Base.ShouldBe(Currency.Usd);
        ev.Quote.ShouldBe(Currency.Amd);
        ev.Rate.ShouldBe(400m);
    }

    [Fact]
    public void FromObservation_in_reversed_direction_stores_the_reciprocal()
    {
        // Observed AMD→USD = 0.0025; canonical USD→AMD must be 400.
        var rate = ExchangeRate.FromObservation(Currency.Amd, Currency.Usd, 0.0025m, T1, RateSource.Exchange);

        rate.Id.ShouldBe(CurrencyPair.Canonical(Currency.Usd, Currency.Amd));
        rate.Rate.ShouldBe(400m);
    }

    [Fact]
    public void Observe_newer_observation_wins()
    {
        var rate = ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, T1, RateSource.Exchange);
        rate.ClearDomainEvents();

        rate.Observe(Currency.Usd, Currency.Amd, 410m, T2, RateSource.Manual);

        rate.Rate.ShouldBe(410m);
        rate.ObservedAt.ShouldBe(T2);
        rate.Source.ShouldBe(RateSource.Manual);
        rate.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExchangeRateChanged>().Rate.ShouldBe(410m);
    }

    [Fact]
    public void Observe_older_observation_is_ignored()
    {
        var rate = ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 410m, T2, RateSource.Manual);
        rate.ClearDomainEvents();

        rate.Observe(Currency.Usd, Currency.Amd, 999m, T1, RateSource.Exchange);

        rate.Rate.ShouldBe(410m);
        rate.ObservedAt.ShouldBe(T2);
        rate.Source.ShouldBe(RateSource.Manual);
        rate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Observe_same_instant_applies_so_a_manual_override_wins()
    {
        var rate = ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, T1, RateSource.Exchange);
        rate.ClearDomainEvents();

        rate.Observe(Currency.Usd, Currency.Amd, 420m, T1, RateSource.Manual);

        rate.Rate.ShouldBe(420m);
        rate.Source.ShouldBe(RateSource.Manual);
    }

    [Fact]
    public void Observe_unchanged_rate_updates_provenance_without_raising_event()
    {
        var rate = ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, T1, RateSource.Exchange);
        rate.ClearDomainEvents();

        rate.Observe(Currency.Usd, Currency.Amd, 400m, T2, RateSource.Manual);

        rate.Rate.ShouldBe(400m);
        rate.ObservedAt.ShouldBe(T2);
        rate.Source.ShouldBe(RateSource.Manual);
        rate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Observe_rejects_a_different_pair()
    {
        var rate = ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, T1, RateSource.Exchange);

        Should.Throw<DomainException>(() =>
            rate.Observe(Currency.Usd, Currency.Usdt, 0.99m, T2, RateSource.Manual));
    }

    [Fact]
    public void Observe_rejects_non_positive_rate()
    {
        var rate = ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, T1, RateSource.Exchange);

        Should.Throw<DomainException>(() =>
            rate.Observe(Currency.Usd, Currency.Amd, 0m, T2, RateSource.Manual));
    }
}
