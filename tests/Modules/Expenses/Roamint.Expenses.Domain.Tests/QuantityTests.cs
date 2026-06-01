using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain.Tests;

public class QuantityTests
{
    [Theory]
    [InlineData(QuantityUnit.Grams)]
    [InlineData(QuantityUnit.Milliliters)]
    [InlineData(QuantityUnit.Pieces)]
    public void Of_keeps_value_and_unit(QuantityUnit unit)
    {
        var quantity = Quantity.Of(2.5m, unit);

        quantity.Value.ShouldBe(2.5m);
        quantity.Unit.ShouldBe(unit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Of_rejects_non_positive_value(decimal value)
    {
        Should.Throw<DomainException>(() => Quantity.Of(value, QuantityUnit.Grams));
    }

    [Fact]
    public void Equality_is_by_value_and_unit()
    {
        Quantity.Of(500m, QuantityUnit.Grams).ShouldBe(Quantity.Of(500m, QuantityUnit.Grams));
        Quantity.Of(500m, QuantityUnit.Grams).ShouldNotBe(Quantity.Of(500m, QuantityUnit.Milliliters));
        Quantity.Of(500m, QuantityUnit.Grams).ShouldNotBe(Quantity.Of(250m, QuantityUnit.Grams));
    }

    [Theory]
    [InlineData(QuantityUnit.Grams, "g")]
    [InlineData(QuantityUnit.Milliliters, "ml")]
    [InlineData(QuantityUnit.Pieces, "Pcs")]
    public void Code_round_trips(QuantityUnit unit, string code)
    {
        unit.ToCode().ShouldBe(code);
        QuantityUnits.FromCode(code).ShouldBe(unit);
    }

    [Fact]
    public void FromCode_rejects_unknown_code()
    {
        Should.Throw<DomainException>(() => QuantityUnits.FromCode("kg"));
    }
}
