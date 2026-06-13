namespace Roamint.Wallet.Contracts;

/// <summary>
/// The Wallet module's public write API. The web host depends only on this interface and the request DTOs
/// below — strongly-typed ids and internal commands stay inside the module. Business-rule violations
/// surface as the kernel's <c>DomainException</c>.
/// </summary>
public interface IWalletApi
{
    /// <summary>Records a currency swap and stamps the implied current rate for its pair. Returns the id.</summary>
    Task<Guid> RecordExchangeAsync(RecordExchangeRequest request, CancellationToken ct = default);

    /// <summary>Manually sets a rate, USD-anchored ("how many quote per 1 USD"). Wins over older observations.</summary>
    Task SetRateAsync(SetRateRequest request, CancellationToken ct = default);
}

/// <summary>A swap to record: gave <see cref="FromAmount"/> <see cref="FromCurrencyCode"/>, received
/// <see cref="ToAmount"/> <see cref="ToCurrencyCode"/>, on <see cref="OccurredOn"/>.</summary>
public sealed record RecordExchangeRequest(
    decimal FromAmount,
    string FromCurrencyCode,
    decimal ToAmount,
    string ToCurrencyCode,
    DateOnly OccurredOn);

/// <summary>A manual rate, USD-anchored: <see cref="QuotePerUsd"/> units of <see cref="QuoteCurrencyCode"/>
/// per one USD (e.g. <c>("AMD", 400m)</c>).</summary>
public sealed record SetRateRequest(string QuoteCurrencyCode, decimal QuotePerUsd);
