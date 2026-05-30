using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).HasColumnName("id");

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasMaxLength(Category.MaxNameLength)
            .IsRequired();

        builder.Property(category => category.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Ignore(category => category.DomainEvents);
    }
}
