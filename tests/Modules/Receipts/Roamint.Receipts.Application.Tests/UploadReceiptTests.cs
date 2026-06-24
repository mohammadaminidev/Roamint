using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Tests;

public sealed class UploadReceiptTests
{
    private static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero));

    private static readonly byte[] Image = [0x01, 0x02, 0x03];

    [Fact]
    public async Task Stores_image_and_lands_an_uploaded_receipt_without_running_ocr()
    {
        var receipts = new InMemoryReceiptRepository();
        var images = new FakeImageStore();
        var uow = new FakeUnitOfWork();
        var handler = new UploadReceiptHandler(receipts, images, uow, NewClock());

        var id = await handler.HandleAsync(new UploadReceipt(Image, "image/jpeg"), TestContext.Current.CancellationToken);

        images.SaveCount.ShouldBe(1);
        uow.SaveCount.ShouldBe(1);
        var stored = receipts.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(id);
        // Upload no longer runs OCR (0019) — the receipt rests as Uploaded with no draft until ParseReceipt.
        stored.Status.ShouldBe(ReceiptStatus.Uploaded);
        stored.Draft.ShouldBeNull();
    }
}
