using Microsoft.Extensions.DependencyInjection;

namespace Roamint.Application.Abstractions.Internal;

/// <summary>Resolves the one-and-only handler of type <typeparamref name="THandler"/>, or throws.</summary>
internal static class Single
{
    public static THandler Resolve<THandler>(IServiceProvider provider, Type messageType)
        where THandler : class
    {
        THandler? found = null;
        var count = 0;
        foreach (var handler in provider.GetServices<THandler>())
        {
            found = handler;
            if (++count > 1)
            {
                throw DispatcherException.MultipleHandlers(messageType);
            }
        }

        return found ?? throw DispatcherException.NoHandler(messageType);
    }
}
