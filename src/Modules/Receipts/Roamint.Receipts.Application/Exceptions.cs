using Roamint.Receipts.Domain;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Application;

/// <summary>A referenced <see cref="Receipt"/> does not exist. An orchestration outcome, not an aggregate
/// invariant, so it lives in the application layer rather than the domain.</summary>
public sealed class ReceiptNotFoundException(ReceiptId id)
    : DomainException($"Receipt '{id.Value}' was not found.")
{
    public ReceiptId ReceiptId { get; } = id;
}
