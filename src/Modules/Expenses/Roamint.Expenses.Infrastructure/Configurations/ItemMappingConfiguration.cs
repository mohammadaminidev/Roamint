using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure.Configurations;

internal sealed class ItemMappingConfiguration : IEntityTypeConfiguration<ItemMapping>
{
    // bge-m3 embedding dimension (verified in the 0016 smoke test).
    public const int EmbeddingDimensions = 1024;

    public void Configure(EntityTypeBuilder<ItemMapping> builder)
    {
        builder.ToTable("item_mappings");

        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.Id).HasColumnName("id");

        builder.Property(mapping => mapping.SourceText)
            .HasColumnName("source_text")
            .HasMaxLength(ItemMapping.MaxSourceLength)
            .IsRequired();

        builder.Property(mapping => mapping.CanonicalDescription)
            .HasColumnName("canonical_description")
            .HasMaxLength(ItemMapping.MaxDescriptionLength)
            .IsRequired();

        builder.Property(mapping => mapping.Unit).HasColumnName("unit").HasMaxLength(8);
        builder.Property(mapping => mapping.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(mapping => mapping.LastUsedOnUtc).HasColumnName("last_used_on_utc").IsRequired();

        // The embedding is an Infrastructure shadow column (the domain holds no vector). Reads use a raw
        // nearest-neighbour query; this mapping lets EF write/migrate the column.
        builder.Property<Vector>("Embedding").HasColumnName("embedding").HasColumnType($"vector({EmbeddingDimensions})");

        // Write-side dedup key (recognition for reads is by embedding, not this).
        builder.HasIndex(mapping => mapping.SourceText).HasDatabaseName("ix_item_mappings_source_text");

        builder.Ignore(mapping => mapping.DomainEvents);
    }
}
