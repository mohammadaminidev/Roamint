using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application;

/// <summary>Write-side access to the <see cref="Category"/> aggregate.</summary>
public interface ICategoryRepository
{
    Task<Category?> GetAsync(CategoryId id, CancellationToken ct);

    Task AddAsync(Category category, CancellationToken ct);
}

/// <summary>Write-side access to the <see cref="Tag"/> aggregate.</summary>
public interface ITagRepository
{
    Task<Tag?> GetAsync(TagId id, CancellationToken ct);

    /// <summary>Batch-load the referenced tags (scope validation loads all of a line set's tags at once).</summary>
    Task<IReadOnlyList<Tag>> GetManyAsync(IReadOnlyCollection<TagId> ids, CancellationToken ct);

    /// <summary>The active tag matching <paramref name="name"/> (case-insensitive) within
    /// <paramref name="scope"/>, or null — backs per-scope uniqueness and idempotent create.</summary>
    Task<Tag?> FindActiveByNameAsync(string name, CategoryId? scope, CancellationToken ct);

    Task AddAsync(Tag tag, CancellationToken ct);
}

/// <summary>Write-side access to the <see cref="Expense"/> aggregate.</summary>
public interface IExpenseRepository
{
    Task<Expense?> GetAsync(ExpenseId id, CancellationToken ct);

    Task AddAsync(Expense expense, CancellationToken ct);
}

/// <summary>Write-side access to learned <see cref="ItemMapping"/>s. The embedding is supplied alongside the
/// aggregate — it is an Infrastructure concern (the domain holds no vector) and is now kept only for Insights.
/// Upsert is by normalised source text, which is also the exact key reads recognise by (see <c>MatchItem</c>).</summary>
public interface IItemMappingRepository
{
    Task<ItemMapping?> FindBySourceTextAsync(string normalizedSourceText, CancellationToken ct);

    Task AddAsync(ItemMapping mapping, float[] embedding, CancellationToken ct);

    /// <summary>Persist a revised mapping's recomputed embedding.</summary>
    Task UpdateEmbeddingAsync(ItemMapping mapping, float[] embedding, CancellationToken ct);
}

/// <summary>Produces an embedding vector for item text (local bge-m3 by default; see 0016). Only the learning
/// handler (<c>RecordItemMapping</c>) depends on this now — matching is exact (0021); the vector is kept for
/// Insights. The model and endpoint are an Infrastructure concern.</summary>
public interface IItemEmbedder
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}

/// <summary>Commits the work of a single use case. The transactional outbox will hook here, persisting
/// buffered domain events alongside the aggregate in one transaction.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
