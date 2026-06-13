using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;
using Roamint.Wallet.Application;
using Roamint.Wallet.Domain;
using Roamint.Wallet.Infrastructure;

namespace Roamint.Wallet.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class GetCurrentRatesHandlerTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Returns_each_current_rate_in_canonical_form()
    {
        var clock = new FakeTimeProvider(Now);

        await using var db = fixture.CreateContext(clock);
        await db.ExchangeRates.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        db.ExchangeRates.Add(ExchangeRate.FromObservation(Currency.Usd, Currency.Amd, 400m, Now, RateSource.Exchange));
        db.ExchangeRates.Add(ExchangeRate.FromObservation(Currency.Usd, Currency.Usdt, 0.99m, Now, RateSource.Manual));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetCurrentRatesHandler(db);
        var rates = await handler.HandleAsync(new GetCurrentRates(), TestContext.Current.CancellationToken);

        rates.Count.ShouldBe(2);

        var usdAmd = rates.Single(rate => rate.QuoteCurrencyCode == "AMD");
        usdAmd.BaseCurrencyCode.ShouldBe("USD");
        usdAmd.Rate.ShouldBe(400m);
        usdAmd.Source.ShouldBe("Exchange");

        var usdUsdt = rates.Single(rate => rate.QuoteCurrencyCode == "USDT");
        usdUsdt.BaseCurrencyCode.ShouldBe("USD");
        usdUsdt.Rate.ShouldBe(0.99m);
        usdUsdt.Source.ShouldBe("Manual");
    }

    [Fact]
    public async Task Returns_empty_when_no_rates_recorded()
    {
        var clock = new FakeTimeProvider(Now);

        await using var db = fixture.CreateContext(clock);
        await db.ExchangeRates.ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        var handler = new GetCurrentRatesHandler(db);
        var rates = await handler.HandleAsync(new GetCurrentRates(), TestContext.Current.CancellationToken);

        rates.ShouldBeEmpty();
    }
}
