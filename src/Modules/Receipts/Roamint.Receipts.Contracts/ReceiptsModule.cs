using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Roamint.Application.Abstractions;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Infrastructure;

namespace Roamint.Receipts.Contracts;

/// <summary>Composition root for the Receipts module — the only Receipts symbol the web host wires.</summary>
public static class ReceiptsModule
{
    public static IHostApplicationBuilder AddReceiptsModule(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddReceiptsPersistence();

        // Command handlers live in Application; the read-model query handler (GetReceiptHandler) lives in
        // Infrastructure. Scan both — the dispatcher registers idempotently.
        builder.Services.AddApplicationDispatcher(
            typeof(UploadReceipt).Assembly,
            typeof(ReceiptsPersistence).Assembly);

        builder.Services.AddScoped<IReceiptsApi, ReceiptsApi>();
        builder.Services.AddScoped<IReceiptsQueries, ReceiptsQueries>();
        builder.Services.AddScoped<IImportsApi, ImportsApi>();

        return builder;
    }
}
