using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure;

internal sealed class ReceiptIdConverter : ValueConverter<ReceiptId, Guid>
{
    public ReceiptIdConverter() : base(id => id.Value, value => new ReceiptId(value)) { }
}

internal sealed class ImportedOrderIdConverter : ValueConverter<ImportedOrderId, Guid>
{
    public ImportedOrderIdConverter() : base(id => id.Value, value => new ImportedOrderId(value)) { }
}

internal sealed class InboxItemIdConverter : ValueConverter<InboxItemId, Guid>
{
    public InboxItemIdConverter() : base(id => id.Value, value => new InboxItemId(value)) { }
}

/// <summary>
/// Persists the OCR <see cref="ParsedDraft"/> as a single <c>jsonb</c> column. The draft is a document we
/// only ever read back whole (for the review screen), never query inside in SQL, so a converter beats a
/// relational owned mapping (and sidesteps the EF-10 owned-collection limits noted in CLAUDE.md). EF applies
/// the converter only to non-null values; a null draft stores SQL NULL.
/// </summary>
internal sealed class ParsedDraftConverter : ValueConverter<ParsedDraft, string>
{
    public ParsedDraftConverter()
        : base(
            draft => JsonSerializer.Serialize(draft, Options),
            json => JsonSerializer.Deserialize<ParsedDraft>(json, Options)!)
    {
    }

    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Change-tracking comparer for the converted <see cref="ParsedDraft"/>. The draft is an immutable
/// value object, so equality is by serialized content and the snapshot is the same instance.</summary>
internal sealed class ParsedDraftComparer : ValueComparer<ParsedDraft>
{
    public ParsedDraftComparer()
        : base(
            (a, b) => Serialize(a) == Serialize(b),
            draft => Serialize(draft).GetHashCode(StringComparison.Ordinal),
            draft => draft)
    {
    }

    private static string Serialize(ParsedDraft? draft) =>
        draft is null ? string.Empty : JsonSerializer.Serialize(draft, ParsedDraftConverter.Options);
}
