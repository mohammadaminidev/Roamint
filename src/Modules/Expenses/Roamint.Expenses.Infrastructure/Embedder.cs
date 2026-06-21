using Microsoft.Extensions.AI;
using Roamint.Expenses.Application;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// <see cref="IItemEmbedder"/> over a Microsoft.Extensions.AI <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// — local bge-m3 via Ollama's OpenAI-compatible endpoint by default (see 0016), config-swappable to any
/// OpenAI-compatible embeddings provider. Stateless; the underlying HTTP client is built once at wiring.
/// </summary>
internal sealed class OllamaItemEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator) : IItemEmbedder
{
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var embeddings = await generator.GenerateAsync([text], cancellationToken: ct);
        return embeddings[0].Vector.ToArray();
    }
}
