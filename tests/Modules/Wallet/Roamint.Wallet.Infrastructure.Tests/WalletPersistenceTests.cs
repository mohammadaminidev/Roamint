using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;
using Roamint.Wallet.Domain;
using Roamint.Wallet.Infrastructure;

namespace Roamint.Wallet.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class WalletPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Exchange_round_trips_with_both_money_columns()
    {
        var clock = new FakeTimeProvider(Now);
        var id = ExchangeId.New(clock);
        var date = new DateOnly(2026, 4, 28);

        await using (var db = fixture.CreateContext(clock))
        {
            db.Exchanges.Add(Exchange.Record(
                id, Money.Of(100m, Currency.Usd), Money.Of(40_000m, Currency.Amd), date, clock));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var loaded = await db.Exchanges.FirstOrDefaultAsync(
                exchange => exchange.Id == id, TestContext.Current.CancellationToken);

            loaded.ShouldNotBeNull();
            loaded.From.ShouldBe(Money.Of(100m, Currency.Usd));
            loaded.To.ShouldBe(Money.Of(40_000m, Currency.Amd));
            loaded.OccurredOn.ShouldBe(date);
            loaded.ImpliedRate.ShouldBe(400m);
        }
    }

    [Fact]
    public async Task ExchangeRate_round_trips_and_is_queryable_by_canonical_pair()
    {
        var clock = new FakeTimeProvider(Now);
        var pair = CurrencyPair.Canonical(Currency.Usd, Currency.Amd);

        await using (var db = fixture.CreateContext(clock))
        {
            await db.ExchangeRates.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            db.ExchangeRates.Add(ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, Now, RateSource.Exchange));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var repo = new ExchangeRateRepository(db);

            // Reversed-direction lookup must resolve to the same canonical row.
            var loaded = await repo.GetAsync(CurrencyPair.Canonical(Currency.Amd, Currency.Usd),
                TestContext.Current.CancellationToken);

            loaded.ShouldNotBeNull();
            loaded.Id.ShouldBe(pair);
            loaded.Rate.ShouldBe(400m);
            loaded.Source.ShouldBe(RateSource.Exchange);
            loaded.ObservedAt.ShouldBe(Now);
        }
    }

    [Fact]
    public async Task Observing_a_tracked_rate_persists_the_update()
    {
        var clock = new FakeTimeProvider(Now);
        var later = Now.AddHours(1);

        await using (var db = fixture.CreateContext(clock))
        {
            await db.ExchangeRates.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            db.ExchangeRates.Add(ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, Now, RateSource.Exchange));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var repo = new ExchangeRateRepository(db);
            var rate = await repo.GetAsync(CurrencyPair.Canonical(Currency.Usd, Currency.Amd),
                TestContext.Current.CancellationToken);
            rate!.Observe(Currency.Usd, Currency.Amd, 420m, later, RateSource.Manual);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var reloaded = await db.ExchangeRates.FirstAsync(TestContext.Current.CancellationToken);
            reloaded.Rate.ShouldBe(420m);
            reloaded.Source.ShouldBe(RateSource.Manual);
        }
    }
}
