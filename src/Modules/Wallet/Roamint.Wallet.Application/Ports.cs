using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Application;

/// <summary>Write-side access to the <see cref="Exchange"/> aggregate.</summary>
public interface IExchangeRepository
{
    Task AddAsync(Exchange exchange, CancellationToken ct);
}

/// <summary>Write-side access to the <see cref="ExchangeRate"/> aggregate (keyed by canonical pair).</summary>
public interface IExchangeRateRepository
{
    Task<ExchangeRate?> GetAsync(CurrencyPair pair, CancellationToken ct);

    Task AddAsync(ExchangeRate rate, CancellationToken ct);
}

/// <summary>Commits the work of a single use case. The transactional outbox hooks here, persisting buffered
/// domain events alongside the aggregate in one transaction.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
