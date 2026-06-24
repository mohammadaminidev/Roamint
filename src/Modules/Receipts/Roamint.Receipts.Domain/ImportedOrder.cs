using Roamint.SharedKernel;

namespace Roamint.Receipts.Domain;

/// <summary>
/// An order pasted from an external shop (e.g. Yerevan City) as structured JSON, parsed into the shared
/// <see cref="ParsedDraft"/> and reviewed like a receipt. Mirrors <see cref="Receipt"/> minus the image and
/// the OCR-failure state: a payload either matches a parser or is rejected before this aggregate exists, so
/// the only states are <see cref="ImportedOrderStatus.Imported"/> → <see cref="ImportedOrderStatus.Linked"/>.
/// <see cref="Source"/> + <see cref="ExternalOrderId"/> are the dedup key — re-pasting a known order is caught
/// rather than double-spent. Like <see cref="Receipt"/>, a single private ctor lets EF bind state through
/// backing fields (see 0010).
/// </summary>
public sealed class ImportedOrder : AggregateRoot<ImportedOrderId>
{
    private ImportedOrder(ImportedOrderId id) : base(id) { }

    /// <summary>The parser that recognized the payload, e.g. <c>"YerevanCity"</c>. Part of the dedup key.</summary>
    public string Source { get; private set; } = null!;

    /// <summary>The shop's own order id (e.g. <c>"AC0000122700"</c>). Dedup key with <see cref="Source"/>.</summary>
    public string ExternalOrderId { get; private set; } = null!;

    public ParsedDraft Draft { get; private set; } = null!;

    public ImportedOrderStatus Status { get; private set; }

    /// <summary>The recorded expense, set on <see cref="LinkTo"/>. Opaque — Receipts owns no Expenses types.</summary>
    public Guid? LinkedExpenseId { get; private set; }

    public DateTimeOffset ImportedOnUtc { get; private set; }

    public static ImportedOrder Import(
        ImportedOrderId id, string source, string externalOrderId, ParsedDraft draft, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new DomainException("An imported order must name the source that produced it.");
        }

        if (string.IsNullOrWhiteSpace(externalOrderId))
        {
            throw new DomainException("An imported order must carry the source's own order id.");
        }

        if (draft.Lines.Count == 0)
        {
            throw new DomainException("An imported order must have at least one line.");
        }

        var now = clock.GetUtcNow();
        var order = new ImportedOrder(id)
        {
            Source = source,
            ExternalOrderId = externalOrderId,
            Draft = draft,
            Status = ImportedOrderStatus.Imported,
            ImportedOnUtc = now,
        };
        order.Raise(new OrderImported(id, source, externalOrderId, now));
        return order;
    }

    public void LinkTo(Guid expenseId)
    {
        if (expenseId == Guid.Empty)
        {
            throw new DomainException("An imported order must link to a real expense.");
        }

        if (Status != ImportedOrderStatus.Imported)
        {
            throw new DomainException($"Cannot link an order in status {Status}; expected {ImportedOrderStatus.Imported}.");
        }

        LinkedExpenseId = expenseId;
        Status = ImportedOrderStatus.Linked;
        Raise(new ImportedOrderLinked(Id, expenseId));
    }
}
