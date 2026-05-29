using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Roamint.Application.Abstractions.Internal;

internal sealed class CommandDispatcher(IServiceProvider provider) : ICommandDispatcher
{
    private static readonly ConcurrentDictionary<Type, VoidWrapper> VoidCache = new();
    private static readonly ConcurrentDictionary<Type, object> ResultCache = new();

    public Task SendAsync(ICommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = VoidCache.GetOrAdd(
            command.GetType(),
            static t => (VoidWrapper)Activator.CreateInstance(typeof(VoidWrapperImpl<>).MakeGenericType(t))!);
        return wrapper.Handle(command, provider, ct);
    }

    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (ResultWrapper<TResult>)ResultCache.GetOrAdd(
            command.GetType(),
            static t => Activator.CreateInstance(typeof(ResultWrapperImpl<,>).MakeGenericType(t, typeof(TResult)))!);
        return wrapper.Handle(command, provider, ct);
    }

    private abstract class VoidWrapper
    {
        public abstract Task Handle(ICommand command, IServiceProvider provider, CancellationToken ct);
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated via Activator.CreateInstance.")]
    private sealed class VoidWrapperImpl<TCommand> : VoidWrapper
        where TCommand : ICommand
    {
        public override Task Handle(ICommand command, IServiceProvider provider, CancellationToken ct)
        {
            var handler = Single.Resolve<ICommandHandler<TCommand>>(provider, typeof(TCommand));
            return handler.HandleAsync((TCommand)command, ct);
        }
    }

    private abstract class ResultWrapper<TResult>
    {
        public abstract Task<TResult> Handle(ICommand<TResult> command, IServiceProvider provider, CancellationToken ct);
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated via Activator.CreateInstance.")]
    private sealed class ResultWrapperImpl<TCommand, TResult> : ResultWrapper<TResult>
        where TCommand : ICommand<TResult>
    {
        public override Task<TResult> Handle(ICommand<TResult> command, IServiceProvider provider, CancellationToken ct)
        {
            var handler = Single.Resolve<ICommandHandler<TCommand, TResult>>(provider, typeof(TCommand));
            return handler.HandleAsync((TCommand)command, ct);
        }
    }
}
