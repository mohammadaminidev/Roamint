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

/// <summary>Write-side access to the <see cref="InboxItem"/> aggregate, plus the dedup lookup the accept handler
/// uses so a re-drained message is caught before it becomes a second item.</summary>
public interface IInboxItemRepository
{
    Task<InboxItem?> GetAsync(InboxItemId id, CancellationToken ct);

    Task<InboxItem?> FindAsync(string source, string externalId, CancellationToken ct);

    Task AddAsync(InboxItem item, CancellationToken ct);
}

/// <summary>What the extractor read from one free-text message. Every field is best-effort: the handler
/// validates and degrades bad values to "needs review" rather than trusting them.</summary>
public sealed record InboxExtraction(
    bool IsExpense,
    decimal? Amount,
    string? CurrencyCode,
    string? Description,
    DateOnly? Date);

/// <summary>Reads a household chat message (Persian free text, 0024) into an <see cref="InboxExtraction"/>.
/// <paramref name="sentOn"/> is the message's local date, the anchor for relative words like "tonight". The
/// LLM adapter lives in Infrastructure; failures throw and become a failed item.</summary>
public interface IInboxExtractor
{
    Task<InboxExtraction> ExtractAsync(string text, DateOnly sentOn, CancellationToken ct);
}

/// <summary>The status an inbox item shows at its source (0024): on Telegram, the bot's reaction on the
/// message. <see cref="None"/> clears it (chatter).</summary>
public enum InboxReaction
{
    None,
    Recorded,
    NeedsReview,
    Failed,
}

/// <summary>Feedback to the source an item came from. Telegram in Infrastructure; a no-op when no bot token is
/// configured. Best-effort by contract — callers must not let a failed notification undo recorded work.</summary>
public interface IInboxNotifier
{
    Task ReactAsync(InboxItem item, InboxReaction reaction, CancellationToken ct);

    Task ReplyAsync(InboxItem item, string text, CancellationToken ct);
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

/// <summary>An image read back from the store: the bytes plus the content type recovered from the stored key's
/// extension, so a re-read can be handed straight to OCR.</summary>
public sealed record StoredImage(byte[] Bytes, string ContentType);

/// <summary>Stores receipt image bytes and returns an opaque key, and reads them back by that key. Local
/// gitignored filesystem in dev; object storage in prod (deferred). The read path (0019) lets the background
/// parse worker re-open the image from the key rather than carrying bytes through the job queue.</summary>
public interface IReceiptImageStore
{
    Task<string> SaveAsync(byte[] image, string contentType, CancellationToken ct);

    /// <summary>Opens a previously-stored image by its key; throws if the key is unknown.</summary>
    Task<StoredImage> OpenAsync(string key, CancellationToken ct);
}

/// <summary>Extracts a <see cref="ParsedDraft"/> from a receipt image. The Anthropic vision adapter lives in
/// Infrastructure; a stub returns <see cref="ParsedDraft.Empty"/> when no API key is configured.</summary>
public interface IReceiptOcr
{
    Task<ParsedDraft> ExtractAsync(byte[] image, string contentType, CancellationToken ct);
}
