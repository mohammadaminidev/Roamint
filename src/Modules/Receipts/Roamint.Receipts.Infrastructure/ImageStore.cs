using Roamint.Receipts.Application;

namespace Roamint.Receipts.Infrastructure;

/// <summary>
/// Stores receipt images as files under a configured root (gitignored in dev). Returns the file name as the
/// opaque key. Object storage is a production concern, deferred (see 0015).
/// </summary>
internal sealed class FileSystemReceiptImageStore(string root) : IReceiptImageStore
{
    public async Task<string> SaveAsync(byte[] image, string contentType, CancellationToken ct)
    {
        Directory.CreateDirectory(root);

        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            _ => ".jpg",
        };

        var key = $"{Guid.CreateVersion7():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(root, key), image, ct);
        return key;
    }

    public async Task<StoredImage> OpenAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, key), ct);

        var contentType = Path.GetExtension(key) switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            _ => "image/jpeg",
        };

        return new StoredImage(bytes, contentType);
    }
}
