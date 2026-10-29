using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure.Configurations;

internal sealed class InboxItemConfiguration : IEntityTypeConfiguration<InboxItem>
{
    public void Configure(EntityTypeBuilder<InboxItem> builder)
    {
        builder.ToTable("inbox_items");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");

        builder.Property(item => item.Source).HasColumnName("source").HasMaxLength(InboxItem.MaxSourceLength).IsRequired();
        builder.Property(item => item.ExternalId)
            .HasColumnName("external_id").HasMaxLength(InboxItem.MaxExternalIdLength).IsRequired();
        builder.Property(item => item.SenderExternalId).HasColumnName("sender_external_id").HasMaxLength(64);
        builder.Property(item => item.Text).HasColumnName("text").HasMaxLength(InboxItem.MaxTextLength).IsRequired();
        builder.Property(item => item.SentOnUtc).HasColumnName("sent_on_utc").IsRequired();
        builder.Property(item => item.ReceivedOnUtc).HasColumnName("received_on_utc").IsRequired();

        // The raw relay payload, kept verbatim for re-reading; never queried inside.
        builder.Property(item => item.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

        builder.Property(item => item.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // Nullable draft, same converter as the receipt's (see ReceiptConfiguration).
        builder.Property(item => item.Draft)
            .HasColumnName("draft")
            .HasColumnType("jsonb")
            .HasConversion((ValueConverter)new ParsedDraftConverter(), new ParsedDraftComparer());

        builder.Property(item => item.LinkedExpenseId).HasColumnName("linked_expense_id");

        // Dedup backstop behind the accept handler's pre-check: a re-drained message can't become a second item.
        builder.HasIndex(item => new { item.Source, item.ExternalId })
            .HasDatabaseName("ux_inbox_items_source_external_id")
            .IsUnique();

        builder.Ignore(item => item.DomainEvents);
    }
}
