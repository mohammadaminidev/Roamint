using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Application;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Tests;

public sealed class UploadReceiptTests
{
    private static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero));

    private static readonly byte[] Image = [0x01, 0x02, 0x03];

    private static ParsedDraft SampleDraft() => new(
        "Yerevan Market", new DateOnly(2026, 6, 19), "AMD", [new ParsedLine("Bread", 450m)]);

    [Fact]
    public async Task Stores_image_parses_draft_and_saves_once_when_ocr_succeeds()
    {
        var receipts = new InMemoryReceiptRepository();
        var images = new FakeImageStore();
        var uow = new FakeUnitOfWork();
        var draft = SampleDraft();
        var handler = new UploadReceiptHandler(receipts, images, new StubOcr(draft), uow, NewClock());

        var id = await handler.HandleAsync(new UploadReceipt(Image, "image/jpeg"), TestContext.Current.CancellationToken);

        images.SaveCount.ShouldBe(1);
        uow.SaveCount.ShouldBe(1);
        var stored = receipts.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(id);
        stored.Status.ShouldBe(ReceiptStatus.Parsed);
        stored.Draft.ShouldBe(draft);
    }

    [Fact]
    public async Task Marks_failed_and_still_saves_when_ocr_throws()
    {
        var receipts = new InMemoryReceiptRepository();
        var uow = new FakeUnitOfWork();
        var handler = new UploadReceiptHandler(receipts, new FakeImageStore(), new ThrowingOcr(), uow, NewClock());

        var id = await handler.HandleAsync(new UploadReceipt(Image, "image/jpeg"), TestContext.Current.CancellationToken);

        uow.SaveCount.ShouldBe(1);
        var stored = receipts.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(id);
        stored.Status.ShouldBe(ReceiptStatus.Failed);
        stored.Draft.ShouldBeNull();
    }

    [Fact]
    public async Task Empty_draft_from_stub_still_parses()
    {
        var receipts = new InMemoryReceiptRepository();
        var handler = new UploadReceiptHandler(
            receipts, new FakeImageStore(), new StubOcr(ParsedDraft.Empty), new FakeUnitOfWork(), NewClock());

        await handler.HandleAsync(new UploadReceipt(Image, "image/jpeg"), TestContext.Current.CancellationToken);

        var stored = receipts.All.ShouldHaveSingleItem();
        stored.Status.ShouldBe(ReceiptStatus.Parsed);
        stored.Draft!.Lines.ShouldBeEmpty();
    }
}
