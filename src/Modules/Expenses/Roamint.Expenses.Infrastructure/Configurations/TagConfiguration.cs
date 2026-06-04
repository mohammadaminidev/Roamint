using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure.Configurations;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");

        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Id).HasColumnName("id");

        builder.Property(tag => tag.Name)
            .HasColumnName("name")
            .HasMaxLength(Tag.MaxNameLength)
            .IsRequired();

        // Nullable scope: a plain converted id column with no DB foreign key — the same id-reference
        // convention used for expense_line.category_id (0009 C1). Integrity is app-enforced.
        builder.Property(tag => tag.Scope).HasColumnName("scope_category_id");

        builder.Property(tag => tag.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Ignore(tag => tag.IsGlobal);
        builder.Ignore(tag => tag.DomainEvents);
    }
}
