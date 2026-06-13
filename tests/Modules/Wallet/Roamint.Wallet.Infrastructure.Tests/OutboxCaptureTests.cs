using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;
using Roamint.Wallet.Domain;
using Roamint.Wallet.Infrastructure;

namespace Roamint.Wallet.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class OutboxCaptureTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Saving_aggregates_captures_one_outbox_row_per_event_and_clears_the_buffer()
    {
        var clock = new FakeTimeProvider(Now);
        var date = new DateOnly(2026, 4, 28);

        await using var db = fixture.CreateContext(clock);
        // Clear both: the rate-changed payload carries no exchange id, so we assert over the whole (now
        // freshly emptied) outbox. The collection runs sequentially, so nothing else writes concurrently.
        await db.OutboxMessages.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.ExchangeRates.ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        var exchange = Exchange.Record(
            ExchangeId.New(clock), Money.Of(100m, Currency.Usd), Money.Of(40_000m, Currency.Amd), date, clock);
        var rate = ExchangeRate.FromObservation(
            Currency.Usd, Currency.Amd, exchange.ImpliedRate, Now, RateSource.Exchange);

        db.Exchanges.Add(exchange);
        db.ExchangeRates.Add(rate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Buffers drained after the save.
        exchange.DomainEvents.ShouldBeEmpty();
        rate.DomainEvents.ShouldBeEmpty();

        var rows = await db.OutboxMessages.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        rows.Select(row => row.Type).ShouldBe(
            ["wallet.exchange-recorded", "wallet.exchange-rate-changed"], ignoreOrder: true);
        rows.ShouldAllBe(row => row.ProcessedOnUtc == null);
        rows.ShouldAllBe(row => row.OccurredOnUtc == Now);
    }

    [Fact]
    public async Task Rolls_back_the_aggregate_and_its_outbox_rows_together()
    {
        var clock = new FakeTimeProvider(Now);
        var id = ExchangeId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        await using (var tx = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            db.Exchanges.Add(Exchange.Record(
                id, Money.Of(50m, Currency.Usd), Money.Of(49m, Currency.Usdt), new DateOnly(2026, 4, 27), clock));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            (await db.Exchanges.AnyAsync(exchange => exchange.Id == id, TestContext.Current.CancellationToken))
                .ShouldBeFalse();

            var all = await db.OutboxMessages.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
            all.ShouldNotContain(row => row.Payload.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
