using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application;

/// <summary>Write-side access to the <see cref="Receipt"/> aggregate.</summary>
public interface IReceiptRepository
{
    Task<Receipt?> GetAsync(ReceiptId id, CancellationToken ct);

    Task AddAsync(Receipt receipt, CancellationToken ct);
}

/// <summary>Commits the work of a single use case. The transactional outbox hooks here, persisting buffered
/// domain events alongside the aggregate in one transaction.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Stores receipt image bytes and returns an opaque key. Local gitignored filesystem in dev;
/// object storage in prod (deferred).</summary>
public interface IReceiptImageStore
{
    Task<string> SaveAsync(byte[] image, string contentType, CancellationToken ct);
}

/// <summary>Extracts a <see cref="ParsedDraft"/> from a receipt image. The Anthropic vision adapter lives in
/// Infrastructure; a stub returns <see cref="ParsedDraft.Empty"/> when no API key is configured.</summary>
public interface IReceiptOcr
{
    Task<ParsedDraft> ExtractAsync(byte[] image, string contentType, CancellationToken ct);
}
