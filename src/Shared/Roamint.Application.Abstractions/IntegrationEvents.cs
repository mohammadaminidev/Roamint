using System.Diagnostics.CodeAnalysis;

namespace Roamint.Application.Abstractions;

/// <summary>
/// A fact published across module boundaries. Distinct from the kernel's <c>IDomainEvent</c>: domain
/// events are internal to an aggregate's unit of work; the outbox translates selected ones into these.
/// </summary>
public interface IIntegrationEvent;

/// <summary>Reacts to an <typeparamref name="TEvent"/>. Any number may be registered; all are invoked.</summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Handler is the ubiquitous DDD term; kept consistent with ICommandHandler/IQueryHandler.")]
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}
