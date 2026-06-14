using Roamint.Application.Abstractions;

namespace Roamint.Wallet.Application;

/// <summary>
/// One current rate, quoted <c>Base→Quote</c> in the canonical pair direction (Base is the higher-priority
/// currency). <c>Source</c> is the canonical code (<c>"Exchange"</c>/<c>"Manual"</c>). The host derives any
/// <c>X→D</c> conversion from the full set (direct, reciprocal, or USD-triangulated).
/// </summary>
public sealed record RateView(
    string BaseCurrencyCode,
    string QuoteCurrencyCode,
    decimal Rate,
    DateTimeOffset ObservedAt,
    string Source);

/// <summary>Lists every current exchange rate. Handled by a read model in Infrastructure.</summary>
public sealed record GetCurrentRates : IQuery<IReadOnlyList<RateView>>;
