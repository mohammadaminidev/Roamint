using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Infrastructure.Configurations;

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates");

        // The canonical CurrencyPair key persists as a single "BASE/QUOTE" text column via CurrencyPairConverter.
        builder.HasKey(rate => rate.Id);
        builder.Property(rate => rate.Id).HasColumnName("pair").HasMaxLength(16);

        builder.Property(rate => rate.Rate).HasColumnName("rate").HasPrecision(18, 8).IsRequired();
        builder.Property(rate => rate.ObservedAt).HasColumnName("observed_at").IsRequired();

        builder.Property(rate => rate.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Ignore(rate => rate.DomainEvents);
    }
}
