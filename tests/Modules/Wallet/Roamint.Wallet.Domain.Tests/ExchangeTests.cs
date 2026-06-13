using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;

namespace Roamint.Wallet.Domain.Tests;

public class ExchangeTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-05-29T12:00:00Z", CultureInfo.InvariantCulture));
    private static readonly DateOnly Today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);

    [Fact]
    public void Record_happy_path_stores_swap_and_raises_event()
    {
        var id = ExchangeId.New(Clock);
        var from = Money.Of(100m, Currency.Usd);
        var to = Money.Of(40_000m, Currency.Amd);

        var exchange = Exchange.Record(id, from, to, Today, Clock);

        exchange.From.ShouldBe(from);
        exchange.To.ShouldBe(to);
        exchange.OccurredOn.ShouldBe(Today);

        var ev = exchange.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExchangeRecorded>();
        ev.ExchangeId.ShouldBe(id);
        ev.From.ShouldBe(from);
        ev.To.ShouldBe(to);
        ev.OccurredOn.ShouldBe(Today);
    }

    [Fact]
    public void ImpliedRate_is_received_over_given()
    {
        var exchange = Exchange.Record(
            ExchangeId.New(Clock), Money.Of(100m, Currency.Usd), Money.Of(40_000m, Currency.Amd), Today, Clock);

        exchange.ImpliedRate.ShouldBe(400m);
    }

    [Fact]
    public void Record_rejects_same_currency()
    {
        Should.Throw<DomainException>(() => Exchange.Record(
            ExchangeId.New(Clock), Money.Of(100m, Currency.Usd), Money.Of(99m, Currency.Usd), Today, Clock));
    }

    [Fact]
    public void Record_rejects_non_positive_amount()
    {
        Should.Throw<DomainException>(() => Exchange.Record(
            ExchangeId.New(Clock), Money.Of(0m, Currency.Usd), Money.Of(40_000m, Currency.Amd), Today, Clock));
    }

    [Fact]
    public void Record_rejects_future_date()
    {
        Should.Throw<DomainException>(() => Exchange.Record(
            ExchangeId.New(Clock), Money.Of(100m, Currency.Usd), Money.Of(40_000m, Currency.Amd), Today.AddDays(1), Clock));
    }
}
