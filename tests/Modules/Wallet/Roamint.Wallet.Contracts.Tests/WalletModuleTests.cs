using Microsoft.Extensions.DependencyInjection;
using Roamint.SharedKernel;
using Roamint.Wallet.Contracts;

namespace Roamint.Wallet.Contracts.Tests;

[Collection(nameof(ModuleHostCollection))]
public sealed class WalletModuleTests(ModuleHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Each test uses a distinct currency pair so they don't clobber each other on the shared database.

    [Fact]
    public async Task Records_an_exchange_and_reads_the_implied_rate_back()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IWalletApi>();
        var queries = scope.ServiceProvider.GetRequiredService<IWalletQueries>();

        await api.RecordExchangeAsync(new RecordExchangeRequest(100m, "USD", 40_000m, "AMD", new DateOnly(2026, 4, 28)), Ct);

        var rates = await queries.GetCurrentRatesAsync(Ct);
        var usdAmd = rates.Single(rate => rate.BaseCurrencyCode == "USD" && rate.QuoteCurrencyCode == "AMD");
        usdAmd.Rate.ShouldBe(400m);
        usdAmd.Source.ShouldBe("Exchange");
    }

    [Fact]
    public async Task Manual_rate_is_usd_anchored_and_reads_back()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IWalletApi>();
        var queries = scope.ServiceProvider.GetRequiredService<IWalletQueries>();

        await api.SetRateAsync(new SetRateRequest("USDT", 0.99m), Ct);

        var rates = await queries.GetCurrentRatesAsync(Ct);
        var usdUsdt = rates.Single(rate => rate.BaseCurrencyCode == "USD" && rate.QuoteCurrencyCode == "USDT");
        usdUsdt.Rate.ShouldBe(0.99m);
        usdUsdt.Source.ShouldBe("Manual");
    }

    [Fact]
    public async Task Reversed_direction_swap_is_canonicalized_through_the_module()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IWalletApi>();
        var queries = scope.ServiceProvider.GetRequiredService<IWalletQueries>();

        // Gave AMD, received USDT: AMD→USDT = 0.0025; canonical USDT→AMD must be 400.
        await api.RecordExchangeAsync(new RecordExchangeRequest(40_000m, "AMD", 100m, "USDT", new DateOnly(2026, 4, 28)), Ct);

        var rates = await queries.GetCurrentRatesAsync(Ct);
        var usdtAmd = rates.Single(rate => rate.BaseCurrencyCode == "USDT" && rate.QuoteCurrencyCode == "AMD");
        usdtAmd.Rate.ShouldBe(400m);
    }

    [Fact]
    public async Task Unknown_currency_code_surfaces_a_DomainException()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IWalletApi>();

        await Should.ThrowAsync<DomainException>(() =>
            api.RecordExchangeAsync(new RecordExchangeRequest(100m, "GBP", 40_000m, "AMD", new DateOnly(2026, 4, 28)), Ct));
    }
}
