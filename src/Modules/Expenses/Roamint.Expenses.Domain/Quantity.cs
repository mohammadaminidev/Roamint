using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain;

/// <summary>
/// The unit a <see cref="Quantity"/> is measured in. A closed, canonical set — one unit per dimension
/// (mass / volume / count) — so recorded quantities never need conversion and aggregate directly
/// (e.g. "Food: 3500 g + 8 Pcs"). The user always records grams and millilitres, never kg or litres.
/// </summary>
public enum QuantityUnit
{
    /// <summary>Mass, in grams ("g").</summary>
    Grams,

    /// <summary>Volume, in millilitres ("ml").</summary>
    Milliliters,

    /// <summary>Count, in pieces ("Pcs").</summary>
    Pieces,
}

/// <summary>Canonical code for each <see cref="QuantityUnit"/> — the single source of truth shared by
/// persistence, contracts, and UI (mirrors <c>Currency.Code</c>).</summary>
public static class QuantityUnits
{
    public static string ToCode(this QuantityUnit unit) => unit switch
    {
        QuantityUnit.Grams => "g",
        QuantityUnit.Milliliters => "ml",
        QuantityUnit.Pieces => "Pcs",
        _ => throw new DomainException($"Unsupported quantity unit '{unit}'."),
    };

    public static QuantityUnit FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return code.Trim() switch
        {
            "g" => QuantityUnit.Grams,
            "ml" => QuantityUnit.Milliliters,
            "Pcs" => QuantityUnit.Pieces,
            _ => throw new DomainException($"Unsupported quantity unit code '{code}'."),
        };
    }
}

/// <summary>
/// An optional, reportable measure on an <see cref="ExpenseLine"/> — how much of the item was bought
/// (e.g. <c>500 g</c>, <c>2 Pcs</c>). It is annotation for analytics, never part of the money
/// arithmetic: the line's <see cref="ExpenseLine.Amount"/> is what was paid, independent of quantity.
/// A reference type so it can map as an optional owned value on the line (see 0008 D4b/D5).
/// </summary>
public sealed record Quantity
{
    private Quantity(decimal value, QuantityUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; private init; }

    public QuantityUnit Unit { get; private init; }

    public static Quantity Of(decimal value, QuantityUnit unit)
    {
        if (value <= 0m)
        {
            throw new DomainException("Quantity value must be greater than zero.");
        }

        return new Quantity(value, unit);
    }

    public override string ToString() => $"{Value} {Unit.ToCode()}";
}
