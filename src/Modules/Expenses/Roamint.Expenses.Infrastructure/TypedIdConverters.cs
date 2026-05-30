using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Infrastructure;

internal sealed class CategoryIdConverter : ValueConverter<CategoryId, Guid>
{
    public CategoryIdConverter() : base(id => id.Value, value => new CategoryId(value)) { }
}

internal sealed class ExpenseIdConverter : ValueConverter<ExpenseId, Guid>
{
    public ExpenseIdConverter() : base(id => id.Value, value => new ExpenseId(value)) { }
}

internal sealed class ExpenseLineIdConverter : ValueConverter<ExpenseLineId, Guid>
{
    public ExpenseLineIdConverter() : base(id => id.Value, value => new ExpenseLineId(value)) { }
}
