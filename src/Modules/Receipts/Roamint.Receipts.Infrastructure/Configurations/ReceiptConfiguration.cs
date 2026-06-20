using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipts");

        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Id).HasColumnName("id");

        builder.Property(receipt => receipt.ImageKey).HasColumnName("image_key").HasMaxLength(512).IsRequired();

        builder.Property(receipt => receipt.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // The OCR draft is a document read back whole for the review screen — one jsonb column via converter.
        // Cast to the base ValueConverter so the non-generic overload binds: the property is nullable
        // (ParsedDraft?) but the converter targets the non-null type, and EF handles the null at the column.
        builder.Property(receipt => receipt.Draft)
            .HasColumnName("draft")
            .HasColumnType("jsonb")
            .HasConversion((ValueConverter)new ParsedDraftConverter(), new ParsedDraftComparer());

        builder.Property(receipt => receipt.LinkedExpenseId).HasColumnName("linked_expense_id");
        builder.Property(receipt => receipt.UploadedOnUtc).HasColumnName("uploaded_on_utc").IsRequired();
        builder.Property(receipt => receipt.ParsedOnUtc).HasColumnName("parsed_on_utc");

        builder.Ignore(receipt => receipt.DomainEvents);
    }
}
