using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Roamint.Application.Abstractions;
using Roamint.Wallet.Application;
using Roamint.Wallet.Infrastructure;

namespace Roamint.Wallet.Contracts;

/// <summary>Composition root for the Wallet module — the only Wallet symbol the web host wires.</summary>
public static class WalletModule
{
    public static IHostApplicationBuilder AddWalletModule(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddWalletPersistence();

        // Command handlers live in Application; the read-model query handler lives in Infrastructure
        // (GetCurrentRatesHandler). Scan both — the dispatcher registers idempotently.
        builder.Services.AddApplicationDispatcher(
            typeof(RecordExchange).Assembly,
            typeof(WalletPersistence).Assembly);

        builder.Services.AddScoped<IWalletApi, WalletApi>();
        builder.Services.AddScoped<IWalletQueries, WalletQueries>();

        return builder;
    }
}
