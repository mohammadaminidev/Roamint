using Microsoft.EntityFrameworkCore;
using Roamint.Wallet.Application;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Infrastructure;

internal sealed class ExchangeRepository(WalletDbContext db) : IExchangeRepository
{
    public Task AddAsync(Exchange exchange, CancellationToken ct)
    {
        // IDs are app-assigned (Guid v7), so no async value generation — plain Add suffices.
        db.Exchanges.Add(exchange);
        return Task.CompletedTask;
    }
}

internal sealed class ExchangeRateRepository(WalletDbContext db) : IExchangeRateRepository
{
    public Task<ExchangeRate?> GetAsync(CurrencyPair pair, CancellationToken ct) =>
        db.ExchangeRates.FirstOrDefaultAsync(rate => rate.Id == pair, ct);

    public Task AddAsync(ExchangeRate rate, CancellationToken ct)
    {
        db.ExchangeRates.Add(rate);
        return Task.CompletedTask;
    }
}

internal sealed class UnitOfWork(WalletDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
