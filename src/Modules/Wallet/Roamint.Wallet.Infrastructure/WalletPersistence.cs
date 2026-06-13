using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Roamint.Wallet.Application;

namespace Roamint.Wallet.Infrastructure;

/// <summary>
/// Persistence wiring for the Wallet module. Public so the module's <c>Contracts</c> project can compose it
/// from <c>AddWalletModule</c>; keeps EF Core / Npgsql / Aspire references out of Contracts.
/// </summary>
public static class WalletPersistence
{
    public static IHostApplicationBuilder AddWalletPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.AddNpgsqlDbContext<WalletDbContext>("roamintdb");

        builder.Services.AddScoped<IExchangeRepository, ExchangeRepository>();
        builder.Services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        builder.Services.AddHostedService<WalletMigrationService>();

        return builder;
    }
}
