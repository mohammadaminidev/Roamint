namespace Roamint.SharedKernel;

/// <summary>
/// A supported currency. A closed smart-enum rather than a <see cref="System.Globalization"/> type:
/// the set is small and fixed, and USDT is not an ISO currency.
/// </summary>
public sealed class Currency : IEquatable<Currency>
{
    /// <summary>US dollar (cash).</summary>
    public static readonly Currency Usd = new("USD", 2);

    /// <summary>Armenian dram (cash).</summary>
    public static readonly Currency Amd = new("AMD", 0);

    /// <summary>Tether (USDT stablecoin), tracked to two places like a fiat cash balance.</summary>
    public static readonly Currency Usdt = new("USDT", 2);

    /// <summary>Iranian toman — a display-only currency, quoted in whole units (1 toman = 10 rial,
    /// stored directly as toman, never an expense's original currency).</summary>
    public static readonly Currency Irt = new("IRT", 0);

    private static readonly Currency[] All = [Usd, Amd, Usdt, Irt];

    private Currency(string code, int minorUnits)
    {
        Code = code;
        MinorUnits = minorUnits;
    }

    /// <summary>ISO-style code, e.g. <c>USD</c>. Always upper-case.</summary>
    public string Code { get; }

    /// <summary>Number of decimal places the currency is tracked to.</summary>
    public int MinorUnits { get; }

    /// <summary>Resolves a currency by code (case-insensitive), throwing if unsupported.</summary>
    public static Currency FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var normalized = code.Trim().ToUpperInvariant();
        foreach (var currency in All)
        {
            if (currency.Code == normalized)
            {
                return currency;
            }
        }

        throw new DomainException($"Unsupported currency code '{code}'.");
    }

    public bool Equals(Currency? other) => other is not null && Code == other.Code;

    public override bool Equals(object? obj) => Equals(obj as Currency);

    public override int GetHashCode() => Code.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Code;

    public static bool operator ==(Currency? left, Currency? right) => Equals(left, right);

    public static bool operator !=(Currency? left, Currency? right) => !Equals(left, right);
}
