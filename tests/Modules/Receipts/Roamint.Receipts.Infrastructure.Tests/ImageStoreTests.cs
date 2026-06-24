using Roamint.Receipts.Infrastructure;

namespace Roamint.Receipts.Infrastructure.Tests;

public sealed class ImageStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"roamint-img-{Guid.CreateVersion7():N}");

    private static readonly byte[] Bytes = [0x11, 0x22, 0x33, 0x44];

    [Fact]
    public async Task Save_then_open_round_trips_bytes_and_recovers_content_type()
    {
        var store = new FileSystemReceiptImageStore(_root);

        var key = await store.SaveAsync(Bytes, "image/png", TestContext.Current.CancellationToken);
        var opened = await store.OpenAsync(key, TestContext.Current.CancellationToken);

        opened.Bytes.ShouldBe(Bytes);
        opened.ContentType.ShouldBe("image/png"); // recovered from the stored key's extension.
    }

    [Fact]
    public async Task Open_throws_for_an_unknown_key()
    {
        var store = new FileSystemReceiptImageStore(_root);

        // Missing file vs. missing root both surface as IOException subtypes — either is a valid "unknown key".
        await Should.ThrowAsync<IOException>(() =>
            store.OpenAsync("nope.jpg", TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
