using System.Diagnostics.CodeAnalysis;
using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.ItemMappings;

/// <summary>
/// Learns (or updates) the mapping from external item text to a canonical item, with its embedding. Called
/// when the user confirms a receipt-sourced expense; a later confirmation of the same source wins, so a
/// rename re-points the mapping in place.
/// </summary>
public sealed record RecordItemMapping(
    string SourceText,
    string CanonicalDescription,
    string? Unit,
    CategoryId CategoryId) : ICommand;

internal sealed class RecordItemMappingHandler(
    IItemMappingRepository mappings,
    ICategoryRepository categories,
    IItemEmbedder embedder,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RecordItemMapping>
{
    public async Task HandleAsync(RecordItemMapping command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await categories.GetAsync(command.CategoryId, ct)
            ?? throw new CategoryNotFoundException(command.CategoryId);

        var normalized = ItemMapping.Normalize(command.SourceText);

        // The embedding is dormant (kept for Insights; matching is exact-text since 0021), so a missing
        // embedder must not sink the learned mapping. Degrade to a null vector and still record the item.
        var embedding = await TryEmbedAsync(normalized, ct);

        var existing = await mappings.FindBySourceTextAsync(normalized, ct);
        if (existing is null)
        {
            var mapping = ItemMapping.Create(
                ItemMappingId.New(clock),
                command.SourceText,
                command.CanonicalDescription,
                command.Unit,
                command.CategoryId,
                clock);
            await mappings.AddAsync(mapping, embedding, ct);
        }
        else
        {
            existing.Revise(command.CanonicalDescription, command.Unit, command.CategoryId, clock);
            await mappings.UpdateEmbeddingAsync(existing, embedding, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    // Best-effort embed: a down or unreachable embedder yields a null vector rather than aborting the learn.
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The embedding is non-essential (Insights-only, dormant for matching); any embedder "
            + "failure must degrade to a null vector so the mapping is still recorded.")]
    private async Task<float[]?> TryEmbedAsync(string normalized, CancellationToken ct)
    {
        try
        {
            return await embedder.EmbedAsync(normalized, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
