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

        builder.Property(expense => expense.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(expense => expense.Note).HasColumnName("note").HasMaxLength(1000);

        // Currency and Total are derived from the lines; never persisted on the expense.
        builder.Ignore(expense => expense.Currency);
        builder.Ignore(expense => expense.Total);

        // Lines are a regular child entity (not owned) so the line's Money can use the two-column
        // ComplexProperty mapping — see ExpenseLineConfiguration and 0008 D4b.
        builder.HasMany(expense => expense.Lines)
            .WithOne()
            .HasForeignKey("expense_id")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(expense => expense.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(expense => expense.DomainEvents);
    }
}

internal sealed class ExpenseLineConfiguration : IEntityTypeConfiguration<ExpenseLine>
{
    public void Configure(EntityTypeBuilder<ExpenseLine> builder)
    {
        builder.ToTable("expense_lines");

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).HasColumnName("id");

        builder.Property(line => line.Description)
            .HasColumnName("description")
            .HasMaxLength(ExpenseLine.MaxDescriptionLength)
            .IsRequired();

        // Plain converted column, no DB foreign key — the same id-reference convention the expense used
        // for its category before 0008. Referential integrity is held by the app (categories are archived,
        // never deleted) and the validating handler.
        builder.Property(line => line.CategoryId).HasColumnName("category_id").IsRequired();

        // Two-column money mapping so category spend can SUM amounts in SQL.
        builder.ComplexProperty(line => line.Amount, money =>
        {
            money.Property(value => value.Amount).HasColumnName("amount").HasPrecision(19, 4);
            money.Property(value => value.Currency)
                .HasColumnName("currency")
                .HasMaxLength(8)
                .HasConversion(currency => currency.Code, code => Currency.FromCode(code));
        });

        // Optional owned quantity → two queryable columns (value numeric, unit as canonical code).
        builder.OwnsOne(line => line.Quantity, quantity =>
        {
            quantity.Property(value => value.Value).HasColumnName("quantity_value").HasPrecision(18, 4);
            quantity.Property(value => value.Unit)
                .HasColumnName("quantity_unit")
                .HasMaxLength(8)
                .HasConversion(new QuantityUnitConverter());
        });

        builder.Navigation(line => line.Quantity).IsRequired(false);

        // Tags applied to the line → join table expenses.expense_line_tags, owned by the line (cascade with
        // it). tag_id is a plain converted id column with no DB FK to tags and no navigation to the Tag
        // aggregate — the reference-other-aggregates-by-id rule (0009 C1/D2).
        builder.OwnsMany(line => line.Tags, tag =>
        {
            tag.ToTable("expense_line_tags");
            tag.WithOwner().HasForeignKey("expense_line_id");
            tag.Property(lineTag => lineTag.TagId).HasColumnName("tag_id");
            tag.HasKey("expense_line_id", "TagId");
        });

        builder.Navigation(line => line.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
