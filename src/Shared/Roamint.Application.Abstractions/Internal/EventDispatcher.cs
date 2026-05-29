using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Roamint.Application.Abstractions.Internal;

internal sealed class EventDispatcher(IServiceProvider provider) : IEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Wrapper> Cache = new();

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var wrapper = Cache.GetOrAdd(
            integrationEvent.GetType(),
            static t => (Wrapper)Activator.CreateInstance(typeof(WrapperImpl<>).MakeGenericType(t))!);
        return wrapper.Handle(integrationEvent, provider, ct);
    }

    private abstract class Wrapper
    {
        public abstract Task Handle(IIntegrationEvent integrationEvent, IServiceProvider provider, CancellationToken ct);
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated via Activator.CreateInstance.")]
    private sealed class WrapperImpl<TEvent> : Wrapper
        where TEvent : IIntegrationEvent
    {
        public override async Task Handle(IIntegrationEvent integrationEvent, IServiceProvider provider, CancellationToken ct)
        {
            var typed = (TEvent)integrationEvent;
            foreach (var handler in provider.GetServices<IIntegrationEventHandler<TEvent>>())
            {
                await handler.HandleAsync(typed, ct).ConfigureAwait(false);
            }
        }
    }
}
