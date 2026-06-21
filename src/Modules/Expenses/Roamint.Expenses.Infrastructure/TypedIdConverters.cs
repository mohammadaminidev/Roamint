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

internal sealed class TagIdConverter : ValueConverter<TagId, Guid>
{
    public TagIdConverter() : base(id => id.Value, value => new TagId(value)) { }
}

internal sealed class ItemMappingIdConverter : ValueConverter<ItemMappingId, Guid>
{
    public ItemMappingIdConverter() : base(id => id.Value, value => new ItemMappingId(value)) { }
}

/// <summary>Stores a <see cref="QuantityUnit"/> as its canonical code (<c>"g"</c>/<c>"ml"</c>/<c>"Pcs"</c>)
/// so quantity reports read and aggregate on a stable, human-legible column.</summary>
internal sealed class QuantityUnitConverter : ValueConverter<QuantityUnit, string>
{
    public QuantityUnitConverter() : base(unit => unit.ToCode(), code => QuantityUnits.FromCode(code)) { }
}
