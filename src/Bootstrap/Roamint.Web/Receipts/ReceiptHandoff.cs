using Roamint.Receipts.Contracts;

namespace Roamint.Web.Receipts;

/// <summary>
/// Carries a parsed receipt from the upload page to the add-expense form within the same Blazor Server
/// circuit. Scoped to the circuit; the add page consumes and clears it on load. Keeps the two pages
/// decoupled (no module talks to another — the host orchestrates) and avoids threading the draft through
/// the URL.
/// </summary>
public sealed class ReceiptHandoff
{
    public Guid? ReceiptId { get; private set; }

    public ParsedDraftDto? Draft { get; private set; }

    public bool HasPending => ReceiptId is not null;

    public void Set(Guid receiptId, ParsedDraftDto? draft)
    {
        ReceiptId = receiptId;
        Draft = draft;
    }

    public void Clear()
    {
        ReceiptId = null;
        Draft = null;
    }
}
