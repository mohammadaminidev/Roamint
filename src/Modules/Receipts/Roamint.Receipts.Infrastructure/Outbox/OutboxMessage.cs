using System.Text.Json;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Infrastructure.Outbox;

/// <summary>
/// An append-only record of a captured domain event, written in the same transaction as the aggregate.
/// Write-only in this slice — the drain (with the Insights consumer) reads it, translates to integration
/// events, dispatches, and stamps <see cref="ProcessedOnUtc"/>.
/// </summary>
internal sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    private OutboxMessage(Guid id, string type, string payload, DateTimeOffset occurredOnUtc)
    {
        Id = id;
        Type = type;
        Payload = payload;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredOnUtc { get; private set; }

    public DateTimeOffset? ProcessedOnUtc { get; private set; }

    public static OutboxMessage From(IDomainEvent domainEvent, DateTimeOffset occurredOnUtc)
    {
        var type = OutboxEventTypes.DiscriminatorFor(domainEvent);
        var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
        return new OutboxMessage(Guid.CreateVersion7(occurredOnUtc), type, payload, occurredOnUtc);
    }
}
