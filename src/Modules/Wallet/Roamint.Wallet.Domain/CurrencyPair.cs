using Roamint.SharedKernel;

namespace Roamint.Wallet.Domain;

/// <summary>
/// An unordered pair of distinct currencies in canonical form: <see cref="Base"/> is the higher-priority
/// currency (USD &gt; USDT &gt; AMD &gt; IRT), <see cref="Quote"/> the lower. Rates are always quoted
/// Base→Quote, so each pair maps to exactly one stored <see cref="ExchangeRate"/> regardless of direction.
/// </summary>
public readonly record struct CurrencyPair
{
    private CurrencyPair(Currency @base, Currency quote)
    {
        Base = @base;
        Quote = quote;
    }

    public Currency Base { get; }

    public Currency Quote { get; }

    /// <summary>Orders two distinct supported currencies into the canonical Base→Quote pair.</summary>
    public static CurrencyPair Canonical(Currency a, Currency b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a == b)
        {
            throw new DomainException("A currency pair must be between two different currencies.");
        }

        return Priority(a) < Priority(b) ? new CurrencyPair(a, b) : new CurrencyPair(b, a);
    }

    // Lower number = higher priority = the pair's Base. IRT (toman) is display-only: it ranks last, so it is
    // only ever a Quote (e.g. USD/IRT) — never the Base of a pair, never an expense's original currency.
    private static int Priority(Currency currency) => currency.Code switch
    {
        "USD" => 0,
        "USDT" => 1,
        "AMD" => 2,
        "IRT" => 3,
        _ => throw new DomainException($"Currency '{currency.Code}' is not supported for exchange rates."),
    };

    public override string ToString() => $"{Base.Code}/{Quote.Code}";
}
