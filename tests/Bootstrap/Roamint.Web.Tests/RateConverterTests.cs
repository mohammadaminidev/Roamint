using Roamint.SharedKernel;
using Roamint.Wallet.Contracts;
using Roamint.Web.Services;

namespace Roamint.Web.Tests;

public sealed class RateConverterTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private static RateConverter Build(params CurrentRateDto[] rates) => new(rates);

    private static CurrentRateDto Rate(string @base, string quote, decimal rate) =>
        new(@base, quote, rate, At, "Manual");

    [Fact]
    public void Identity_returns_the_same_amount()
    {
        var converter = Build();

        converter.TryConvert(123.45m, Currency.Usd, Currency.Usd, out var converted).ShouldBeTrue();
        converted.ShouldBe(123.45m);
    }

    [Fact]
    public void Direct_pair_converts_and_rounds_to_target_minor_units()
    {
        var converter = Build(Rate("USD", "AMD", 400m));

        // 12.40 USD -> AMD (0 minor units) = 4960.
        converter.TryConvert(12.40m, Currency.Usd, Currency.Amd, out var converted).ShouldBeTrue();
        converted.ShouldBe(4960m);
    }

    [Fact]
    public void Reciprocal_pair_converts_the_other_direction()
    {
        var converter = Build(Rate("USD", "AMD", 400m));

        // 40000 AMD -> USD = 100.00.
        converter.TryConvert(40_000m, Currency.Amd, Currency.Usd, out var converted).ShouldBeTrue();
        converted.ShouldBe(100m);
    }

    [Fact]
    public void Triangulates_through_usd_when_no_direct_pair()
    {
        // Only USD↔AMD and USD↔USDT known; AMD→USDT must go through USD.
        var converter = Build(Rate("USD", "AMD", 400m), Rate("USD", "USDT", 1m));

        // 40000 AMD -> USD (100) -> USDT (100) = 100.00.
        converter.TryConvert(40_000m, Currency.Amd, Currency.Usdt, out var converted).ShouldBeTrue();
        converted.ShouldBe(100m);
    }

    [Fact]
    public void Reports_missing_when_no_path_exists()
    {
        // Only USD↔AMD known; nothing connects USDT.
        var converter = Build(Rate("USD", "AMD", 400m));

        converter.TryConvert(50m, Currency.Usdt, Currency.Amd, out var converted).ShouldBeFalse();
        converted.ShouldBe(0m);
        converter.CanConvert(Currency.Usdt, Currency.Amd).ShouldBeFalse();
    }

    [Fact]
    public void Empty_rate_set_can_only_do_identity()
    {
        var converter = Build();

        converter.CanConvert(Currency.Usd, Currency.Amd).ShouldBeFalse();
        converter.CanConvert(Currency.Amd, Currency.Amd).ShouldBeTrue();
    }
}
