using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Tests;

internal sealed class InMemoryReceiptRepository : IReceiptRepository
{
    private readonly Dictionary<ReceiptId, Receipt> _store = [];

    public Task<Receipt?> GetAsync(ReceiptId id, CancellationToken ct) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task AddAsync(Receipt receipt, CancellationToken ct)
    {
        _store[receipt.Id] = receipt;
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<Receipt> All => _store.Values;
}

internal sealed class FakeImageStore : IReceiptImageStore
{
    private readonly Dictionary<string, StoredImage> _store = [];

    public int SaveCount { get; private set; }

    public int OpenCount { get; private set; }

    public Task<string> SaveAsync(byte[] image, string contentType, CancellationToken ct)
    {
        SaveCount++;
        var key = $"receipts/{Guid.CreateVersion7():N}.bin";
        _store[key] = new StoredImage(image, contentType);
        return Task.FromResult(key);
    }

    public Task<StoredImage> OpenAsync(string key, CancellationToken ct)
    {
        OpenCount++;
        return _store.TryGetValue(key, out var image)
            ? Task.FromResult(image)
            : throw new FileNotFoundException($"No stored image for key '{key}'.");
    }
}

/// <summary>Returns a fixed draft — stands in for a successful vision call (or the empty-draft stub).</summary>
internal sealed class StubOcr(ParsedDraft draft) : IReceiptOcr
{
    public Task<ParsedDraft> ExtractAsync(byte[] image, string contentType, CancellationToken ct) =>
        Task.FromResult(draft);
}

/// <summary>Throws — stands in for an OCR call that could not run (network, auth, malformed image).</summary>
internal sealed class ThrowingOcr : IReceiptOcr
{
    public Task<ParsedDraft> ExtractAsync(byte[] image, string contentType, CancellationToken ct) =>
        throw new InvalidOperationException("vision call failed");
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
