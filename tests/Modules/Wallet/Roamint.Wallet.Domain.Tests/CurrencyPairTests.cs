using Roamint.SharedKernel;

namespace Roamint.Wallet.Domain.Tests;

public class CurrencyPairTests
{
    [Fact]
    public void Canonical_orders_by_priority_regardless_of_argument_order()
    {
        var fromUsdAmd = CurrencyPair.Canonical(Currency.Usd, Currency.Amd);
        var fromAmdUsd = CurrencyPair.Canonical(Currency.Amd, Currency.Usd);

        fromUsdAmd.Base.ShouldBe(Currency.Usd);
        fromUsdAmd.Quote.ShouldBe(Currency.Amd);
        fromAmdUsd.ShouldBe(fromUsdAmd);
    }

    [Fact]
    public void Canonical_ranks_usdt_above_amd()
    {
        var pair = CurrencyPair.Canonical(Currency.Amd, Currency.Usdt);

        pair.Base.ShouldBe(Currency.Usdt);
        pair.Quote.ShouldBe(Currency.Amd);
    }

    [Fact]
    public void Canonical_ranks_usd_above_usdt()
    {
        var pair = CurrencyPair.Canonical(Currency.Usdt, Currency.Usd);

        pair.Base.ShouldBe(Currency.Usd);
        pair.Quote.ShouldBe(Currency.Usdt);
    }

    [Fact]
    public void Canonical_rejects_same_currency()
    {
        Should.Throw<DomainException>(() => CurrencyPair.Canonical(Currency.Usd, Currency.Usd));
    }

    [Fact]
    public void Canonical_rejects_unsupported_currency()
    {
        // IRR is a display-only currency, out of scope for wallet rates.
        Should.Throw<DomainException>(() => CurrencyPair.Canonical(Currency.Usd, Currency.Irr));
    }
}
