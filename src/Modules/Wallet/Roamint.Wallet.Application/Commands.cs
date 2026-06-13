using Roamint.Application.Abstractions;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Application;

/// <summary>
/// Records a currency swap — gave <paramref name="FromAmount"/> <paramref name="FromCurrencyCode"/>,
/// received <paramref name="ToAmount"/> <paramref name="ToCurrencyCode"/>. The implied rate becomes the
/// current rate for the pair (latest-wins). Returns the new exchange's id.
/// </summary>
public sealed record RecordExchange(
    decimal FromAmount,
    string FromCurrencyCode,
    decimal ToAmount,
    string ToCurrencyCode,
    DateOnly OccurredOn) : ICommand<ExchangeId>;

/// <summary>
/// Manually sets a rate, USD-anchored: <paramref name="QuotePerUsd"/> units of
/// <paramref name="QuoteCurrencyCode"/> per one USD. Wins over older observations for the pair.
/// </summary>
public sealed record SetRate(string QuoteCurrencyCode, decimal QuotePerUsd) : ICommand;
