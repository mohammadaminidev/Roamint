using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain;

/// <summary>
/// A learned resolution of external item text (e.g. an Armenian receipt line) to a canonical expense item —
/// the user's final Farsi description, its quantity unit, and its category. Written when the user confirms a
/// receipt-sourced expense; a later confirmation of the same source wins. Recognition for reads is by
/// <b>exact</b> match on the normalised source text (0021); the embedding is still written (Infrastructure
/// concern, deliberately absent here) but kept dormant for Insights, no longer used to match.
/// </summary>
public sealed class ItemMapping : AggregateRoot<ItemMappingId>
{
    public const int MaxSourceLength = 300;
    public const int MaxDescriptionLength = 200;

    // EF / serialization constructor; not for domain use.
    private ItemMapping(ItemMappingId id) : base(id) { }

    /// <summary>The external source text, normalised. The write-side dedup key <i>and</i> the read-side exact
    /// match key (0021).</summary>
    public string SourceText { get; private set; } = string.Empty;

    /// <summary>The user's final canonical description (Farsi).</summary>
    public string CanonicalDescription { get; private set; } = string.Empty;

    /// <summary>Canonical quantity-unit code (<c>g</c>/<c>ml</c>/<c>Pcs</c>) or null.</summary>
    public string? Unit { get; private set; }

    public CategoryId CategoryId { get; private set; }

    public DateTimeOffset LastUsedOnUtc { get; private set; }

    public static ItemMapping Create(
        ItemMappingId id,
        string sourceText,
        string canonicalDescription,
        string? unit,
        CategoryId categoryId,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var mapping = new ItemMapping(id) { SourceText = Normalize(sourceText) };
        mapping.Apply(canonicalDescription, unit, categoryId, clock);
        return mapping;
    }

    /// <summary>Re-point an existing mapping to a (possibly renamed) item — a later confirmation wins. The
    /// source text and identity are unchanged; only the resolution moves.</summary>
    public void Revise(string canonicalDescription, string? unit, CategoryId categoryId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Apply(canonicalDescription, unit, categoryId, clock);
    }

    /// <summary>Trim, collapse internal whitespace, and lowercase. This is the <b>exact match key</b> (0021):
    /// shared by the write-side dedup key and the read-side recognition lookup, so the two never diverge and
    /// case/spacing noise between scans doesn't split the same item.</summary>
    public static string Normalize(string sourceText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);

        var collapsed = string.Join(' ', sourceText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        if (collapsed.Length > MaxSourceLength)
        {
            throw new DomainException($"Item source text must be {MaxSourceLength} characters or fewer.");
        }

        return collapsed;
    }

    private void Apply(string canonicalDescription, string? unit, CategoryId categoryId, TimeProvider clock)
    {
        CanonicalDescription = NormalizeDescription(canonicalDescription);
        Unit = NormalizeUnit(unit);
        CategoryId = categoryId;
        LastUsedOnUtc = clock.GetUtcNow();
    }

    private static string NormalizeDescription(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var clean = description.Trim();
        if (clean.Length > MaxDescriptionLength)
        {
            throw new DomainException($"Item description must be {MaxDescriptionLength} characters or fewer.");
        }

        return clean;
    }

    // Validate against the canonical unit set so only g/ml/Pcs (or null) are ever stored.
    private static string? NormalizeUnit(string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? null : QuantityUnits.FromCode(unit).ToCode();
}
