namespace Roamint.Wallet.Contracts;

/// <summary>
/// The Wallet module's public read API. Returns primitive DTOs; the canonical pair and internal
/// projections stay inside the module.
/// </summary>
public interface IWalletQueries
{
    /// <summary>Every current rate, one row per canonical pair. The host derives any X→D conversion (direct,
    /// reciprocal, or USD-triangulated) from this set.</summary>
    Task<IReadOnlyList<CurrentRateDto>> GetCurrentRatesAsync(CancellationToken ct = default);
}

/// <summary>One current rate, quoted <see cref="BaseCurrencyCode"/>→<see cref="QuoteCurrencyCode"/> (Base is
/// the higher-priority currency). <see cref="Source"/> is <c>"Exchange"</c> or <c>"Manual"</c>.</summary>
public sealed record CurrentRateDto(
    string BaseCurrencyCode,
    string QuoteCurrencyCode,
    decimal Rate,
    DateTimeOffset ObservedAt,
    string Source);
