using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure.Configurations;

internal sealed class ImportedOrderConfiguration : IEntityTypeConfiguration<ImportedOrder>
{
    public void Configure(EntityTypeBuilder<ImportedOrder> builder)
    {
        builder.ToTable("imported_orders");

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).HasColumnName("id");

        builder.Property(order => order.Source).HasColumnName("source").HasMaxLength(64).IsRequired();
        builder.Property(order => order.ExternalOrderId)
            .HasColumnName("external_order_id").HasMaxLength(128).IsRequired();

        builder.Property(order => order.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // The review draft is a document read back whole — one jsonb column via the same converter the
        // receipt uses. Non-nullable here, so the generic overload binds directly.
        builder.Property(order => order.Draft)
            .HasColumnName("draft")
            .HasColumnType("jsonb")
            .HasConversion(new ParsedDraftConverter(), new ParsedDraftComparer())
            .IsRequired();

        builder.Property(order => order.LinkedExpenseId).HasColumnName("linked_expense_id");
        builder.Property(order => order.ImportedOnUtc).HasColumnName("imported_on_utc").IsRequired();

        // Dedup backstop: a re-pasted order (same source + id) is caught by the handler pre-check, and by
        // this unique index if two pastes race.
        builder.HasIndex(order => new { order.Source, order.ExternalOrderId })
            .HasDatabaseName("ux_imported_orders_source_external_id")
            .IsUnique();

        builder.Ignore(order => order.DomainEvents);
    }
}
