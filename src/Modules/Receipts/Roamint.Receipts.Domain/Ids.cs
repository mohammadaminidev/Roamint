namespace Roamint.Receipts.Domain;

/// <summary>Identity of a <see cref="Receipt"/> aggregate.</summary>
public readonly record struct ReceiptId(Guid Value)
{
    public static ReceiptId New(TimeProvider clock) => new(Guid.CreateVersion7(clock.GetUtcNow()));
}
