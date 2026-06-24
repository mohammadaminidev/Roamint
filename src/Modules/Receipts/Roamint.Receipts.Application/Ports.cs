using System.Text.Json;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application;

/// <summary>Write-side access to the <see cref="Receipt"/> aggregate.</summary>
public interface IReceiptRepository
{
    Task<Receipt?> GetAsync(ReceiptId id, CancellationToken ct);

    Task AddAsync(Receipt receipt, CancellationToken ct);
}

/// <summary>Write-side access to the <see cref="ImportedOrder"/> aggregate, plus the dedup lookup the import
/// handler uses to catch a re-pasted order before creating a duplicate.</summary>
public interface IImportedOrderRepository
{
    Task<ImportedOrder?> GetAsync(ImportedOrderId id, CancellationToken ct);

    /// <summary>The existing order for a <c>(source, externalOrderId)</c> pair, or null. Backed by the unique
    /// index, so this is the friendly pre-check, not the only guard.</summary>
    Task<ImportedOrder?> FindAsync(string source, string externalOrderId, CancellationToken ct);

    Task AddAsync(ImportedOrder order, CancellationToken ct);
}

/// <summary>What a parser extracts from a recognized payload: the source's own order id (the dedup key) and
/// the shared review draft.</summary>
public sealed record ParsedOrder(string ExternalOrderId, ParsedDraft Draft);

/// <summary>
/// Recognizes and maps one known external-order JSON shape into a <see cref="ParsedOrder"/>. The import
/// handler holds the full set and asks each <see cref="CanParse"/> in turn — first structural match wins,
/// none-match is an unrecognized source. New shops are a new implementation, not a config edit.
/// </summary>
public interface IOrderImportParser
{
    /// <summary>The stable source tag stamped on the resulting <see cref="ImportedOrder"/>, e.g. <c>"YerevanCity"</c>.</summary>
    string Source { get; }

    /// <summary>True if this parser recognizes the payload's structure. A pure structural fingerprint — no
    /// network, no token.</summary>
    bool CanParse(JsonElement root);

    /// <summary>Maps a payload this parser <see cref="CanParse"/>. May throw a <c>DomainException</c> if a
    /// recognized shape carries unusable data (e.g. a totals mismatch beyond tolerance).</summary>
    ParsedOrder Parse(JsonElement root);
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
