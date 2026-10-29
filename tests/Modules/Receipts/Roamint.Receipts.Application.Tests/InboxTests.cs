using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Application.Inbox;
using Roamint.Receipts.Domain;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Application.Tests;

public sealed class InboxTests
{
    // Drain happens Monday morning; the message was sent Saturday 23:30 Yerevan (19:30 UTC).
    private static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.Zero));

    private static readonly long SaturdayNight = new DateTimeOffset(2026, 10, 24, 19, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static string Update(int messageId, string text = "من 15 هزار درام برای شام امشب دادم", long userId = 777) =>
        $$$"""{"update_id":{{{1000 + messageId}}},"message":{"message_id":{{{messageId}}},"from":{"id":{{{userId}}}},"chat":{"id":-100111},"date":{{{SaturdayNight}}},"text":"{{{text}}}"}}""";

    private static InboxEnvelope Envelope(int messageId, string? payload = null) =>
        new("telegram", $"-100111:{messageId}", payload ?? Update(messageId));

    private static async Task<(InMemoryInboxItemRepository Repo, InboxItemId Id)> AcceptedAsync()
    {
        var repo = new InMemoryInboxItemRepository();
        var ids = await new AcceptInboxEnvelopesHandler(repo, new FakeUnitOfWork(), NewClock())
            .HandleAsync(new AcceptInboxEnvelopes([Envelope(42)]), TestContext.Current.CancellationToken);
        return (repo, ids.ShouldHaveSingleItem());
    }

    [Fact]
    public async Task Accept_reads_text_sender_and_send_time_from_a_telegram_update()
    {
        var (repo, id) = await AcceptedAsync();

        var item = repo.All.ShouldHaveSingleItem();
        item.Id.ShouldBe(id);
        item.Text.ShouldBe("من 15 هزار درام برای شام امشب دادم");
        item.SenderExternalId.ShouldBe("777");
        item.SentOnUtc.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(SaturdayNight));
        item.Status.ShouldBe(InboxItemStatus.Received);
    }

    [Fact]
    public async Task Accept_skips_known_duplicate_and_unreadable_envelopes()
    {
        var repo = new InMemoryInboxItemRepository();
        var uow = new FakeUnitOfWork();
        var handler = new AcceptInboxEnvelopesHandler(repo, uow, NewClock());
        await handler.HandleAsync(new AcceptInboxEnvelopes([Envelope(1)]), TestContext.Current.CancellationToken);

        var ids = await handler.HandleAsync(
            new AcceptInboxEnvelopes(
            [
                Envelope(1),                                  // already accepted
                Envelope(2), Envelope(2),                     // duplicate within the batch
                Envelope(3, "{not json"),                     // unreadable
                Envelope(4, """{"message":{"message_id":4,"date":1}}"""), // no text
                new InboxEnvelope("gwallet", "x", "{}"),      // unknown source
            ]),
            TestContext.Current.CancellationToken);

        ids.Count.ShouldBe(1);
        repo.All.Select(item => item.ExternalId).ShouldBe(["-100111:1", "-100111:2"]);
    }

    [Fact]
    public async Task Parse_builds_a_one_line_draft_anchored_to_the_message_day()
    {
        var (repo, id) = await AcceptedAsync();
        var extractor = new StubExtractor(new InboxExtraction(true, 15000m, "amd", " شام ", null));

        var outcome = await new ParseInboxItemHandler(repo, extractor, new FakeUnitOfWork())
            .HandleAsync(new ParseInboxItem(id), TestContext.Current.CancellationToken);

        // Saturday 23:30 in Yerevan, not the Monday drain and not the UTC date.
        extractor.SentOn.ShouldBe(new DateOnly(2026, 10, 24));
        outcome.Status.ShouldBe(InboxItemStatus.Parsed);
        var draft = outcome.Draft.ShouldNotBeNull();
        draft.Date.ShouldBe(new DateOnly(2026, 10, 24));
        draft.CurrencyCode.ShouldBe("AMD");
        var line = draft.Lines.ShouldHaveSingleItem();
        line.Description.ShouldBe("شام");
        line.SourceText.ShouldBe("شام");
        line.Amount.ShouldBe(15000m);
        repo.All.Single().Status.ShouldBe(InboxItemStatus.Parsed);
    }

    [Fact]
    public async Task Parse_degrades_implausible_values_for_review_instead_of_trusting_them()
    {
        var (repo, id) = await AcceptedAsync();
        var extractor = new StubExtractor(new InboxExtraction(true, -5m, "x", "  ", new DateOnly(2026, 10, 30)));

        var draft = (await new ParseInboxItemHandler(repo, extractor, new FakeUnitOfWork())
            .HandleAsync(new ParseInboxItem(id), TestContext.Current.CancellationToken)).Draft.ShouldNotBeNull();

        draft.CurrencyCode.ShouldBeNull();
        draft.Date.ShouldBe(new DateOnly(2026, 10, 24)); // a date after the message day is discarded
        var line = draft.Lines.ShouldHaveSingleItem();
        line.Amount.ShouldBe(0m);
        line.Description.ShouldBe("من 15 هزار درام برای شام امشب دادم"); // blank description falls back to the text
    }

    [Fact]
    public async Task Parse_keeps_an_earlier_extracted_date()
    {
        var (repo, id) = await AcceptedAsync();
        var extractor = new StubExtractor(new InboxExtraction(true, 10m, "USD", "taxi", new DateOnly(2026, 10, 23)));

        var draft = (await new ParseInboxItemHandler(repo, extractor, new FakeUnitOfWork())
            .HandleAsync(new ParseInboxItem(id), TestContext.Current.CancellationToken)).Draft.ShouldNotBeNull();

        draft.Date.ShouldBe(new DateOnly(2026, 10, 23));
    }

    [Fact]
    public async Task Parse_ignores_chatter()
    {
        var (repo, id) = await AcceptedAsync();

        var outcome = await new ParseInboxItemHandler(
                repo, new StubExtractor(new InboxExtraction(false, null, null, null, null)), new FakeUnitOfWork())
            .HandleAsync(new ParseInboxItem(id), TestContext.Current.CancellationToken);

        outcome.Status.ShouldBe(InboxItemStatus.Ignored);
        outcome.Draft.ShouldBeNull();
        repo.All.Single().Status.ShouldBe(InboxItemStatus.Ignored);
    }

    [Fact]
    public async Task Parse_fails_the_item_when_the_extractor_throws_and_saves_it()
    {
        var (repo, id) = await AcceptedAsync();
        var uow = new FakeUnitOfWork();

        var outcome = await new ParseInboxItemHandler(repo, new ThrowingExtractor(), uow)
            .HandleAsync(new ParseInboxItem(id), TestContext.Current.CancellationToken);

        outcome.Status.ShouldBe(InboxItemStatus.Failed);
        repo.All.Single().Status.ShouldBe(InboxItemStatus.Failed);
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Link_and_ignore_drive_the_aggregate_and_unknown_ids_throw()
    {
        var (repo, id) = await AcceptedAsync();
        await new ParseInboxItemHandler(
                repo, new StubExtractor(new InboxExtraction(true, 1m, "AMD", "x", null)), new FakeUnitOfWork())
            .HandleAsync(new ParseInboxItem(id), TestContext.Current.CancellationToken);
        var expenseId = Guid.CreateVersion7();

        await new LinkInboxItemHandler(repo, new FakeUnitOfWork())
            .HandleAsync(new LinkInboxItem(id, expenseId), TestContext.Current.CancellationToken);

        repo.All.Single().LinkedExpenseId.ShouldBe(expenseId);
        await Should.ThrowAsync<InboxItemNotFoundException>(() => new IgnoreInboxItemHandler(repo, new FakeUnitOfWork())
            .HandleAsync(new IgnoreInboxItem(InboxItemId.New(NewClock())), TestContext.Current.CancellationToken));
        await Should.ThrowAsync<DomainException>(() => new IgnoreInboxItemHandler(repo, new FakeUnitOfWork())
            .HandleAsync(new IgnoreInboxItem(id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task React_and_reply_go_to_the_notifier()
    {
        var (repo, id) = await AcceptedAsync();
        var notifier = new RecordingNotifier();

        await new ReactToInboxItemHandler(repo, notifier)
            .HandleAsync(new ReactToInboxItem(id, InboxReaction.NeedsReview), TestContext.Current.CancellationToken);
        await new ReplyToInboxItemHandler(repo, notifier)
            .HandleAsync(new ReplyToInboxItem(id, "15,000 AMD · Food"), TestContext.Current.CancellationToken);

        notifier.Reactions.ShouldHaveSingleItem().ShouldBe((id, InboxReaction.NeedsReview));
        notifier.Replies.ShouldHaveSingleItem().ShouldBe((id, "15,000 AMD · Food"));
    }
}
