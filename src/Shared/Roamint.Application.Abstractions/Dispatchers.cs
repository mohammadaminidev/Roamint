namespace Roamint.Application.Abstractions;

/// <summary>Routes a command to its single handler.</summary>
public interface ICommandDispatcher
{
    Task SendAsync(ICommand command, CancellationToken ct = default);

    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default);
}

/// <summary>Routes a query to its single handler.</summary>
public interface IQueryDispatcher
{
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}

/// <summary>Publishes an integration event to every registered handler.</summary>
public interface IEventDispatcher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct = default);
}
