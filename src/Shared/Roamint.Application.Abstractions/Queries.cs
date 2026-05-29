namespace Roamint.Application.Abstractions;

/// <summary>A side-effect-free request that returns <typeparamref name="TResult"/>.</summary>
public interface IQuery<TResult>;

/// <summary>Handles a <typeparamref name="TQuery"/>. Exactly one per query type.</summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}
