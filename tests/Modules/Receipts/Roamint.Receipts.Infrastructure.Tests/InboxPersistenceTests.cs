using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Application.Inbox;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class InboxPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SentOn = new(2026, 10, 24, 19, 30, 0, TimeSpan.Zero);

    private static InboxItem NewItem(FakeTimeProvider clock, string externalId) => InboxItem.Receive(
        InboxItemId.New(clock), "telegram", externalId, "777", "15 هزار درام شام", SentOn,
        """{"update_id":1,"message":{"text":"15 هزار درام شام"}}""", clock);

    private static string UniqueExternalId() => $"-100111:{Guid.CreateVersion7():N}";

    [Fact]
    public async Task Round_trips_a_linked_item_with_payload_and_draft_and_captures_its_events()
    {
        var clock = new FakeTimeProvider(Now);
        var externalId = UniqueExternalId();
        var item = NewItem(clock, externalId);
        var expenseId = Guid.CreateVersion7();
        item.Parse(new ParsedDraft(null, new DateOnly(2026, 10, 24), "AMD", [new ParsedLine("شام", 15000m, SourceText: "شام")]));
        item.LinkTo(expenseId);

        await using (var db = fixture.CreateContext(clock))
        {
            db.InboxItems.Add(item);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var types = (await db.OutboxMessages.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken))
                .Where(m => m.Payload.Contains(item.Id.Value.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Type)
                .ToList();
            types.ShouldBe(["receipts.inbox-item-received", "receipts.inbox-item-linked"], ignoreOrder: true);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var loaded = await db.InboxItems.AsNoTracking()
                .SingleAsync(i => i.Id == item.Id, TestContext.Current.CancellationToken);

            loaded.ExternalId.ShouldBe(externalId);
            loaded.SenderExternalId.ShouldBe("777");
            loaded.Text.ShouldBe("15 هزار درام شام");
            loaded.SentOnUtc.ShouldBe(SentOn);
            loaded.Payload.ShouldContain("update_id");
            loaded.Status.ShouldBe(InboxItemStatus.Linked);
            loaded.LinkedExpenseId.ShouldBe(expenseId);
            loaded.Draft.ShouldNotBeNull().Lines.ShouldHaveSingleItem().Amount.ShouldBe(15000m);
        }
    }

    [Fact]
    public async Task Unique_index_rejects_a_second_item_for_the_same_message()
    {
        var clock = new FakeTimeProvider(Now);
        var externalId = UniqueExternalId();

        await using var db = fixture.CreateContext(clock);
        db.InboxItems.Add(NewItem(clock, externalId));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.InboxItems.Add(NewItem(clock, externalId));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Read_models_return_the_item_view_and_list_only_unsettled_items()
    {
        var clock = new FakeTimeProvider(Now);
        var received = NewItem(clock, UniqueExternalId());
        var failed = NewItem(clock, UniqueExternalId());
        failed.MarkFailed();
        var ignored = NewItem(clock, UniqueExternalId());
        ignored.Ignore();

        await using (var db = fixture.CreateContext(clock))
        {
            db.InboxItems.AddRange(received, failed, ignored);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            var view = (await new GetInboxItemHandler(db).HandleAsync(
                new GetInboxItem(received.Id), TestContext.Current.CancellationToken)).ShouldNotBeNull();
            view.Status.ShouldBe("Received");
            view.SenderExternalId.ShouldBe("777");
            view.Draft.ShouldBeNull();

            var open = await new ListOpenInboxItemsHandler(db).HandleAsync(
                new ListOpenInboxItems(), TestContext.Current.CancellationToken);
            open.ShouldContain(received.Id.Value);
            open.ShouldContain(failed.Id.Value);
            open.ShouldNotContain(ignored.Id.Value);
        }
    }
}
