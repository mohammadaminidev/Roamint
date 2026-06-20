using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Roamint.Receipts.Application;

namespace Roamint.Receipts.Infrastructure;

/// <summary>
/// Persistence + OCR wiring for the Receipts module. Public so the module's <c>Contracts</c> project can
/// compose it from <c>AddReceiptsModule</c>; keeps EF Core / Npgsql / AI references out of Contracts.
/// </summary>
public static class ReceiptsPersistence
{
    private const string DefaultOcrEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/";
    private const string DefaultOcrModel = "gemini-2.5-flash";

    public static IHostApplicationBuilder AddReceiptsPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.AddNpgsqlDbContext<ReceiptsDbContext>("roamintdb");

        builder.Services.AddScoped<IReceiptRepository, ReceiptRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        var imageRoot = builder.Configuration["Receipts:ImageRoot"]
            ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "receipts");
        builder.Services.AddSingleton<IReceiptImageStore>(new FileSystemReceiptImageStore(imageRoot));

        AddOcr(builder);

        builder.Services.AddHostedService<ReceiptsMigrationService>();

        return builder;
    }

    private static void AddOcr(IHostApplicationBuilder builder)
    {
        var apiKey = builder.Configuration["Ocr:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No key (e.g. the GPU-less prod host): upload still works, review opens blank.
            builder.Services.AddSingleton<IReceiptOcr, StubReceiptOcr>();
            return;
        }

        var endpoint = builder.Configuration["Ocr:Endpoint"] ?? DefaultOcrEndpoint;
        var model = builder.Configuration["Ocr:Model"] ?? DefaultOcrModel;

        var chat = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetChatClient(model)
            .AsIChatClient();

        builder.Services.AddSingleton(chat);
        builder.Services.AddSingleton<IReceiptOcr, ChatClientReceiptOcr>();
    }
}
