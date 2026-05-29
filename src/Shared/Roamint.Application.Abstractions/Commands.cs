namespace Roamint.Application.Abstractions;

/// <summary>A request that mutates state and returns nothing.</summary>
public interface ICommand;

/// <summary>A request that mutates state and returns a <typeparamref name="TResult"/> (e.g. a new id).</summary>
public interface ICommand<TResult>;

/// <summary>Handles a void <typeparamref name="TCommand"/>. Exactly one per command type.</summary>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken ct);
}

/// <summary>Handles a <typeparamref name="TCommand"/> that returns <typeparamref name="TResult"/>. Exactly one per command type.</summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}
