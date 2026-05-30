using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Configurations;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses");

        builder.HasKey(expense => expense.Id);
        builder.Property(expense => expense.Id).HasColumnName("id");

        builder.Property(expense => expense.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(expense => expense.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(expense => expense.Note).HasColumnName("note").HasMaxLength(1000);

        builder.ComplexProperty(expense => expense.Amount, MapMoney("amount", "currency"));

        builder.OwnsMany(expense => expense.Lines, line =>
        {
            line.ToTable("expense_lines");
            line.WithOwner().HasForeignKey("expense_id");

            line.HasKey(item => item.Id);
            line.Property(item => item.Id).HasColumnName("id");

            line.Property(item => item.Description)
                .HasColumnName("description")
                .HasMaxLength(ExpenseLine.MaxDescriptionLength)
                .IsRequired();

            line.Property(item => item.Quantity).HasColumnName("quantity").HasPrecision(18, 4);

            // ComplexProperty is unavailable on owned-collection builders, so the line price uses a
            // single-column converter. Lines are aggregate-internal and never queried by price.
            line.Property(item => item.UnitPrice)
                .HasColumnName("unit_price")
                .HasConversion(new MoneyConverter())
                .HasMaxLength(40)
                .IsRequired();
        });

        builder.Navigation(expense => expense.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(expense => expense.DomainEvents);
    }

    private static Action<ComplexPropertyBuilder<Money>> MapMoney(string amountColumn, string currencyColumn) =>
        money =>
        {
            money.Property(value => value.Amount).HasColumnName(amountColumn).HasPrecision(19, 4);
            money.Property(value => value.Currency)
                .HasColumnName(currencyColumn)
                .HasMaxLength(8)
                .HasConversion(currency => currency.Code, code => Currency.FromCode(code));
        };
}
