using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class ImportedOrderPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 22, 0, 0, TimeSpan.Zero);

    private static ParsedDraft SampleDraft() => new(
        "Yerevan City",
        new DateOnly(2026, 6, 21),
        "AMD",
        [
            new ParsedLine("Cherry", 1287.04m, 722m, "g", "Cherry"),
            new ParsedLine("Eggs", 990m, 1m, "Pcs", "Eggs"),
        ]);

    [Fact]
    public async Task Round_trips_an_imported_order_with_its_jsonb_draft()
    {
        var clock = new FakeTimeProvider(Now);
        var id = ImportedOrderId.New(clock);

        await using (var db = fixture.CreateContext(clock))
        {
            var order = ImportedOrder.Import(id, "YerevanCity", "AC0000122700", SampleDraft(), clock);
            db.ImportedOrders.Add(order);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var loaded = await db.ImportedOrders.AsNoTracking()
                .SingleAsync(o => o.Id == id, TestContext.Current.CancellationToken);

            loaded.Source.ShouldBe("YerevanCity");
            loaded.ExternalOrderId.ShouldBe("AC0000122700");
            loaded.Status.ShouldBe(ImportedOrderStatus.Imported);
            loaded.Draft.Merchant.ShouldBe("Yerevan City");
            loaded.Draft.Lines.Count.ShouldBe(2);
            loaded.Draft.Lines[0].Description.ShouldBe("Cherry");
            loaded.Draft.Lines[0].Amount.ShouldBe(1287.04m);
            loaded.Draft.Lines[0].QuantityUnit.ShouldBe("g");
        }
    }

    [Fact]
    public async Task Captures_one_outbox_row_per_event()
    {
        var clock = new FakeTimeProvider(Now);
        var id = ImportedOrderId.New(clock);

        await using var db = fixture.CreateContext(clock);
        var order = ImportedOrder.Import(id, "YerevanCity", "AC0000122701", SampleDraft(), clock);
        db.ImportedOrders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        order.DomainEvents.ShouldBeEmpty();

        var rows = await db.OutboxMessages.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        var types = rows
            .Where(m => m.Payload.Contains(id.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Type)
            .ToList();

        types.ShouldBe(["receipts.order-imported"]);
    }

    [Fact]
    public async Task Unique_index_rejects_a_duplicate_source_and_external_id()
    {
        var clock = new FakeTimeProvider(Now);

        await using var db = fixture.CreateContext(clock);
        db.ImportedOrders.Add(
            ImportedOrder.Import(ImportedOrderId.New(clock), "YerevanCity", "AC-DUP", SampleDraft(), clock));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.ImportedOrders.Add(
            ImportedOrder.Import(ImportedOrderId.New(clock), "YerevanCity", "AC-DUP", SampleDraft(), clock));

        await Should.ThrowAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
