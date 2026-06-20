using Roamint.SharedKernel;

namespace Roamint.Receipts.Domain;

/// <summary>
/// A photographed receipt and its OCR lifecycle. An image is <see cref="ReceiptStatus.Uploaded"/>; OCR then
/// either <see cref="ReceiptStatus.Parsed"/> it into a <see cref="ParsedDraft"/> or it
/// <see cref="ReceiptStatus.Failed"/>. After the user reviews the draft and records an expense, the receipt
/// is <see cref="ReceiptStatus.Linked"/> to that expense by an opaque id — no cross-schema FK. Linked is
/// terminal.
/// </summary>
public sealed class Receipt : AggregateRoot<ReceiptId>
{
    // EF / serialization constructor; not for domain use. The factory and transitions set state via the
    // private setters (EF materializes the ParsedDraft and scalars through backing fields), so there is a
    // single, unambiguous constructor for EF to bind — see 0010.
    private Receipt(ReceiptId id) : base(id) { }

    public string ImageKey { get; private set; } = null!;

    public ReceiptStatus Status { get; private set; }

    /// <summary>Populated on <see cref="Parse"/>; null while <see cref="ReceiptStatus.Uploaded"/> or on failure.</summary>
    public ParsedDraft? Draft { get; private set; }

    /// <summary>The recorded expense, set on <see cref="LinkTo"/>. Opaque — Receipts owns no Expenses types.</summary>
    public Guid? LinkedExpenseId { get; private set; }

    public DateTimeOffset UploadedOnUtc { get; private set; }

    public DateTimeOffset? ParsedOnUtc { get; private set; }

    public static Receipt Upload(ReceiptId id, string imageKey, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(imageKey))
        {
            throw new DomainException("A receipt must reference a stored image.");
        }

        var now = clock.GetUtcNow();
        var receipt = new Receipt(id) { ImageKey = imageKey, Status = ReceiptStatus.Uploaded, UploadedOnUtc = now };
        receipt.Raise(new ReceiptUploaded(id, imageKey, now));
        return receipt;
    }

    public void Parse(ParsedDraft draft, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(clock);
        RequireStatus(ReceiptStatus.Uploaded, "parse");

        var now = clock.GetUtcNow();
        Draft = draft;
        ParsedOnUtc = now;
        Status = ReceiptStatus.Parsed;
        Raise(new ReceiptParsed(Id, now));
    }

    public void MarkFailed(string reason)
    {
        RequireStatus(ReceiptStatus.Uploaded, "fail");

        Status = ReceiptStatus.Failed;
        Raise(new ReceiptParseFailed(Id, reason ?? string.Empty));
    }

    public void LinkTo(Guid expenseId)
    {
        if (expenseId == Guid.Empty)
        {
            throw new DomainException("A receipt must link to a real expense.");
        }

        RequireStatus(ReceiptStatus.Parsed, "link");

        LinkedExpenseId = expenseId;
        Status = ReceiptStatus.Linked;
        Raise(new ReceiptLinked(Id, expenseId));
    }

    private void RequireStatus(ReceiptStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new DomainException($"Cannot {action} a receipt in status {Status}; expected {expected}.");
        }
    }
}
