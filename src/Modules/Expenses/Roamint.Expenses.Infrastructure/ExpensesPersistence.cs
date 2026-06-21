using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Pgvector.EntityFrameworkCore;
using Roamint.Expenses.Application;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// Persistence wiring for the Expenses module. Public so the module's <c>Contracts</c> project can compose
/// it from <c>AddExpensesModule</c>; keeps EF Core / Npgsql / Aspire / AI references out of Contracts.
/// </summary>
public static class ExpensesPersistence
{
    private const string DefaultEmbeddingEndpoint = "http://localhost:11434/v1";
    private const string DefaultEmbeddingModel = "bge-m3";

    public static IHostApplicationBuilder AddExpensesPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);

        // UseVector registers the pgvector type on this context's data source — needed for the item-mapping
        // embedding column and the nearest-neighbour read (0016).
        builder.AddNpgsqlDbContext<ExpensesDbContext>(
            "roamintdb",
            configureDbContextOptions: options => options.UseNpgsql(npgsql => npgsql.UseVector()));

        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<ITagRepository, TagRepository>();
        builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
        builder.Services.AddScoped<IItemMappingRepository, ItemMappingRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        AddItemEmbedder(builder);

        builder.Services.AddHostedService<ExpensesMigrationService>();

        return builder;
    }

    // Item embeddings over an OpenAI-compatible endpoint — local bge-m3 via Ollama by default (see 0016).
    // Construction is lazy (just an HTTP client); a missing Ollama only fails when an embed is actually called,
    // which the match/learn callers treat as best-effort.
    private static void AddItemEmbedder(IHostApplicationBuilder builder)
    {
        var endpoint = builder.Configuration["Embeddings:Endpoint"] ?? DefaultEmbeddingEndpoint;
        var model = builder.Configuration["Embeddings:Model"] ?? DefaultEmbeddingModel;
        var apiKey = builder.Configuration["Embeddings:ApiKey"] ?? "ollama"; // Ollama ignores it; the client requires non-empty.

        var generator = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetEmbeddingClient(model)
            .AsIEmbeddingGenerator();

        builder.Services.AddSingleton(generator);
        builder.Services.AddSingleton<IItemEmbedder, OllamaItemEmbedder>();
    }
}
