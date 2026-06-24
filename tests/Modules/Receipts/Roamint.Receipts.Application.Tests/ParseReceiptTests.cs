using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Application;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Tests;

public sealed class ParseReceiptTests
{
    private static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero));

    private static readonly byte[] Image = [0x0A, 0x0B, 0x0C];

    private static ParsedDraft SampleDraft() => new(
        "Yerevan Market", new DateOnly(2026, 6, 19), "AMD", [new ParsedLine("Bread", 450m)]);

    // Stores an image and lands an Uploaded receipt, returning its id and the image key it was stored under.
    private static async Task<ReceiptId> UploadAsync(
        InMemoryReceiptRepository receipts, FakeImageStore images, TimeProvider clock)
    {
        var upload = new UploadReceiptHandler(receipts, images, new FakeUnitOfWork(), clock);
        return await upload.HandleAsync(new UploadReceipt(Image, "image/jpeg"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reopens_image_runs_ocr_and_parses_the_receipt()
    {
        var clock = NewClock();
        var receipts = new InMemoryReceiptRepository();
        var images = new FakeImageStore();
        var id = await UploadAsync(receipts, images, clock);

        var draft = SampleDraft();
        var ocr = new CapturingOcr(draft);
        var uow = new FakeUnitOfWork();
        var handler = new ParseReceiptHandler(receipts, images, ocr, uow, clock);

        var returned = await handler.HandleAsync(new ParseReceipt(id), TestContext.Current.CancellationToken);

        returned.ShouldBe(draft);
        images.OpenCount.ShouldBe(1);
        ocr.SeenBytes.ShouldBe(Image); // the worker parses from the re-read stored image, not carried bytes.
        uow.SaveCount.ShouldBe(1);
        var stored = receipts.All.ShouldHaveSingleItem();
        stored.Status.ShouldBe(ReceiptStatus.Parsed);
        stored.Draft.ShouldBe(draft);
    }

    [Fact]
    public async Task Marks_failed_and_returns_null_when_ocr_throws()
    {
        var clock = NewClock();
        var receipts = new InMemoryReceiptRepository();
        var images = new FakeImageStore();
        var id = await UploadAsync(receipts, images, clock);

        var handler = new ParseReceiptHandler(receipts, images, new ThrowingOcr(), new FakeUnitOfWork(), clock);

        var returned = await handler.HandleAsync(new ParseReceipt(id), TestContext.Current.CancellationToken);

        returned.ShouldBeNull();
        var stored = receipts.All.ShouldHaveSingleItem();
        stored.Status.ShouldBe(ReceiptStatus.Failed);
        stored.Draft.ShouldBeNull();
    }

    [Fact]
    public async Task Throws_when_receipt_is_unknown()
    {
        var clock = NewClock();
        var handler = new ParseReceiptHandler(
            new InMemoryReceiptRepository(), new FakeImageStore(), new StubOcr(SampleDraft()), new FakeUnitOfWork(), clock);

        await Should.ThrowAsync<ReceiptNotFoundException>(() =>
            handler.HandleAsync(new ParseReceipt(ReceiptId.New(clock)), TestContext.Current.CancellationToken));
    }

    /// <summary>Returns a fixed draft and records the bytes it was handed, to prove the image is re-read.</summary>
    private sealed class CapturingOcr(ParsedDraft draft) : IReceiptOcr
    {
        public byte[]? SeenBytes { get; private set; }

        public Task<ParsedDraft> ExtractAsync(byte[] image, string contentType, CancellationToken ct)
        {
            SeenBytes = image;
            return Task.FromResult(draft);
        }
    }
}
