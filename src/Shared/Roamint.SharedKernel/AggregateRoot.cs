namespace Roamint.SharedKernel;

/// <summary>
/// Base for aggregate roots. Buffers domain events raised during a unit of work; the persistence layer
/// drains them (via <see cref="ClearDomainEvents"/>) and forwards them to the outbox after commit.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : struct, IEquatable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id) { }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
