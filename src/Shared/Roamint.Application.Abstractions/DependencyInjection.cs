using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Roamint.Application.Abstractions.Internal;

namespace Roamint.Application.Abstractions;

public static class DependencyInjection
{
    private static readonly Type[] HandlerInterfaces =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IIntegrationEventHandler<>),
    ];

    /// <summary>
    /// Registers the in-process dispatchers and scans <paramref name="handlerAssemblies"/> for command,
    /// query, and integration-event handlers, registering each as scoped.
    /// </summary>
    public static IServiceCollection AddApplicationDispatcher(
        this IServiceCollection services,
        params Assembly[] handlerAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handlerAssemblies);

        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        services.AddScoped<IEventDispatcher, EventDispatcher>();

        foreach (var assembly in handlerAssemblies)
        {
            RegisterHandlers(services, assembly);
        }

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                continue;
            }

            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && HandlerInterfaces.Contains(iface.GetGenericTypeDefinition()))
                {
                    services.AddScoped(iface, type);
                }
            }
        }
    }
}
