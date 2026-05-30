namespace Roamint.SharedKernel;

/// <summary>
/// Non-generic view of an aggregate's buffered domain events, so persistence can drain them without
/// knowing the aggregate's id type. Implemented by <see cref="AggregateRoot{TId}"/>.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
