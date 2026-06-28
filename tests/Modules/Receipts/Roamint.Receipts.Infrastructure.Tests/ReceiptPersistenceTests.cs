using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Domain;
using Roamint.Receipts.Infrastructure;

namespace Roamint.Receipts.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class ReceiptPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Round_trips_a_parsed_receipt_with_its_jsonb_draft()
    {
        var clock = new FakeTimeProvider(Now);
        var id = ReceiptId.New(clock);
        var draft = new ParsedDraft(
            "ԱԼԵՔՍԱՐՍ",
            new DateOnly(2026, 6, 19),
            "AMD",
            [
                new ParsedLine("Պանիր Չանախ", 485m, 0.17m, "կգ"),
                new ParsedLine("Պիցցայի խմոր MG 38", 390m),
            ]);

        await using (var db = fixture.CreateContext(clock))
        {
            var receipt = Receipt.Upload(id, "receipts/abc.jpg", clock);
            receipt.Parse(draft, clock);
            db.Receipts.Add(receipt);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var loaded = await db.Receipts.AsNoTracking()
                .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);

            loaded.Status.ShouldBe(ReceiptStatus.Parsed);
            loaded.ImageKey.ShouldBe("receipts/abc.jpg");
            loaded.Draft.ShouldNotBeNull();
            loaded.Draft!.Merchant.ShouldBe("ԱԼԵՔՍԱՐՍ");
            loaded.Draft.Date.ShouldBe(new DateOnly(2026, 6, 19));
            loaded.Draft.CurrencyCode.ShouldBe("AMD");
            loaded.Draft.Lines.Count.ShouldBe(2);
            loaded.Draft.Lines[0].Description.ShouldBe("Պանիր Չանախ");
            loaded.Draft.Lines[0].Amount.ShouldBe(485m);
            loaded.Draft.Lines[0].QuantityValue.ShouldBe(0.17m);
            loaded.Draft.Lines[0].QuantityUnit.ShouldBe("կգ");
        }
    }

    [Fact]
    public async Task Reads_committed_status_even_when_the_context_tracks_a_stale_copy()
    {
        // Reproduces the 0019 regression: the upload-step context tracks the receipt at Uploaded while the
        // background worker advances it to Parsed in its own context. The shared circuit-scoped context must
        // serve the committed status to the link step, not the stale tracked copy.
        var clock = new FakeTimeProvider(Now);
        var id = ReceiptId.New(clock);

        await using var circuit = fixture.CreateContext(clock);
        var circuitRepo = new ReceiptRepository(circuit);

        // Upload on the "circuit" context — leaves the entity tracked at Uploaded.
        await circuitRepo.AddAsync(Receipt.Upload(id, "receipts/stale.jpg", clock), TestContext.Current.CancellationToken);
        await circuit.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Background worker parses it in a separate context — DB row is now Parsed.
        await using (var worker = fixture.CreateContext(clock))
        {
            var parsing = await new ReceiptRepository(worker).GetAsync(id, TestContext.Current.CancellationToken);
            parsing!.Parse(ParsedDraft.Empty, clock);
            await worker.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Link step on the original circuit context: must see Parsed and link cleanly.
        var linking = await circuitRepo.GetAsync(id, TestContext.Current.CancellationToken);
        linking!.Status.ShouldBe(ReceiptStatus.Parsed);

        Should.NotThrow(() => linking.LinkTo(Guid.NewGuid()));
    }

    [Fact]
    public async Task Captures_one_outbox_row_per_event_and_clears_the_buffer()
    {
        var clock = new FakeTimeProvider(Now);
        var id = ReceiptId.New(clock);

        await using var db = fixture.CreateContext(clock);
        var receipt = Receipt.Upload(id, "receipts/xyz.jpg", clock);
        receipt.Parse(ParsedDraft.Empty, clock);

        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        receipt.DomainEvents.ShouldBeEmpty();

        // Filter in memory: payload is jsonb, so a SQL LIKE on it is invalid (jsonb ~~ jsonb).
        var rows = await db.OutboxMessages.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        var types = rows
            .Where(m => m.Payload.Contains(id.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Type)
            .ToList();

        types.ShouldBe(["receipts.receipt-uploaded", "receipts.receipt-parsed"], ignoreOrder: true);
    }
}
