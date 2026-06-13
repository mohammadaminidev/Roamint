using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Roamint.SharedKernel;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Infrastructure;

internal sealed class ExchangeIdConverter : ValueConverter<ExchangeId, Guid>
{
    public ExchangeIdConverter() : base(id => id.Value, value => new ExchangeId(value)) { }
}

/// <summary>Stores an <see cref="ExchangeRate"/>'s canonical <see cref="CurrencyPair"/> key as a single
/// <c>BASE/QUOTE</c> text column (e.g. <c>USD/AMD</c>), reconstructing it through the canonical factory.</summary>
internal sealed class CurrencyPairConverter : ValueConverter<CurrencyPair, string>
{
    public CurrencyPairConverter()
        : base(pair => pair.Base.Code + "/" + pair.Quote.Code, value => Parse(value)) { }

    private static CurrencyPair Parse(string value)
    {
        var parts = value.Split('/');
        return CurrencyPair.Canonical(Currency.FromCode(parts[0]), Currency.FromCode(parts[1]));
    }
}
