using Roamint.Wallet.Application;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Application.Tests;

internal sealed class InMemoryExchangeRepository : IExchangeRepository
{
    private readonly Dictionary<ExchangeId, Exchange> _store = [];

    public Task AddAsync(Exchange exchange, CancellationToken ct)
    {
        _store[exchange.Id] = exchange;
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<Exchange> All => _store.Values;
}

internal sealed class InMemoryExchangeRateRepository : IExchangeRateRepository
{
    private readonly Dictionary<CurrencyPair, ExchangeRate> _store = [];

    public Task<ExchangeRate?> GetAsync(CurrencyPair pair, CancellationToken ct) =>
        Task.FromResult(_store.GetValueOrDefault(pair));

    public Task AddAsync(ExchangeRate rate, CancellationToken ct)
    {
        _store[rate.Id] = rate;
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<ExchangeRate> All => _store.Values;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
