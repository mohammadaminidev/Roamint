using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Roamint.Application.Abstractions.Internal;

internal sealed class QueryDispatcher(IServiceProvider provider) : IQueryDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Cache = new();

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var wrapper = (Wrapper<TResult>)Cache.GetOrAdd(
            query.GetType(),
            static t => Activator.CreateInstance(typeof(WrapperImpl<,>).MakeGenericType(t, typeof(TResult)))!);
        return wrapper.Handle(query, provider, ct);
    }

    private abstract class Wrapper<TResult>
    {
        public abstract Task<TResult> Handle(IQuery<TResult> query, IServiceProvider provider, CancellationToken ct);
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated via Activator.CreateInstance.")]
    private sealed class WrapperImpl<TQuery, TResult> : Wrapper<TResult>
        where TQuery : IQuery<TResult>
    {
        public override Task<TResult> Handle(IQuery<TResult> query, IServiceProvider provider, CancellationToken ct)
        {
            var handler = Single.Resolve<IQueryHandler<TQuery, TResult>>(provider, typeof(TQuery));
            return handler.HandleAsync((TQuery)query, ct);
        }
    }
}
