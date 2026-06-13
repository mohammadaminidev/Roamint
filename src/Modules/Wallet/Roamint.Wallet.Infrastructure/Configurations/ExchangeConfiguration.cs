using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roamint.SharedKernel;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Infrastructure.Configurations;

internal sealed class ExchangeConfiguration : IEntityTypeConfiguration<Exchange>
{
    public void Configure(EntityTypeBuilder<Exchange> builder)
    {
        builder.ToTable("exchanges");

        builder.HasKey(exchange => exchange.Id);
        builder.Property(exchange => exchange.Id).HasColumnName("id");

        builder.Property(exchange => exchange.OccurredOn).HasColumnName("occurred_on").IsRequired();

        // Two-column money mappings (Exchange is a regular entity, so ComplexProperty applies — 0005).
        builder.ComplexProperty(exchange => exchange.From, money =>
        {
            money.Property(value => value.Amount).HasColumnName("from_amount").HasPrecision(19, 4);
            money.Property(value => value.Currency)
                .HasColumnName("from_currency")
                .HasMaxLength(8)
                .HasConversion(currency => currency.Code, code => Currency.FromCode(code));
        });

        builder.ComplexProperty(exchange => exchange.To, money =>
        {
            money.Property(value => value.Amount).HasColumnName("to_amount").HasPrecision(19, 4);
            money.Property(value => value.Currency)
                .HasColumnName("to_currency")
                .HasMaxLength(8)
                .HasConversion(currency => currency.Code, code => Currency.FromCode(code));
        });

        // Derived from From/To; never persisted.
        builder.Ignore(exchange => exchange.ImpliedRate);
        builder.Ignore(exchange => exchange.DomainEvents);
    }
}
