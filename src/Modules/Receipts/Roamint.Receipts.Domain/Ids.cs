namespace Roamint.Receipts.Domain;

/// <summary>Identity of a <see cref="Receipt"/> aggregate.</summary>
public readonly record struct ReceiptId(Guid Value)
{
    public static ReceiptId New(TimeProvider clock) => new(Guid.CreateVersion7(clock.GetUtcNow()));
}

/// <summary>Identity of an <see cref="ImportedOrder"/> aggregate.</summary>
public readonly record struct ImportedOrderId(Guid Value)
{
    public static ImportedOrderId New(TimeProvider clock) => new(Guid.CreateVersion7(clock.GetUtcNow()));
}

/// <summary>Identity of an <see cref="InboxItem"/> aggregate.</summary>
public readonly record struct InboxItemId(Guid Value)
{
    public static InboxItemId New(TimeProvider clock) => new(Guid.CreateVersion7(clock.GetUtcNow()));
}
