using Roamint.SharedKernel;

namespace Roamint.Wallet.Domain;

/// <summary>
/// The current exchange rate for a <see cref="CurrencyPair"/>, quoted Base→Quote. Updated by recorded
/// exchanges and manual overrides; the most recent observation wins (<see cref="Observe"/>). The stored
/// value is always in the pair's canonical direction — callers may observe either direction and the
/// aggregate normalizes (reciprocal) so there is exactly one value per pair.
/// </summary>
public sealed class ExchangeRate : AggregateRoot<CurrencyPair>
{
    // EF / serialization constructor; not for domain use. State is set via the private setters by the
    // factory and Observe, so EF has a single id-binding constructor (the CurrencyPair key round-trips
    // through its value converter, like the other strongly-typed ids) — see 0010 D-EF.
    private ExchangeRate(CurrencyPair id) : base(id) { }

    /// <summary>Units of <see cref="CurrencyPair.Quote"/> per one unit of <see cref="CurrencyPair.Base"/>.</summary>
    public decimal Rate { get; private set; }

    /// <summary>When the current value was observed — the ordering key for latest-wins.</summary>
    public DateTimeOffset ObservedAt { get; private set; }

    public RateSource Source { get; private set; }

    /// <summary>Creates the rate for a pair from its first observation, in any direction.</summary>
    public static ExchangeRate FromObservation(
        Currency from, Currency to, decimal observedRate, DateTimeOffset observedAt, RateSource source)
    {
        var pair = CurrencyPair.Canonical(from, to);
        var canonical = Normalize(pair, from, observedRate);
        var rate = new ExchangeRate(pair) { Rate = canonical, ObservedAt = observedAt, Source = source };
        rate.Raise(new ExchangeRateChanged(pair.Base, pair.Quote, canonical, observedAt, source));
        return rate;
    }

    /// <summary>
    /// Applies a new observation (in any direction). Latest wins: ignored when older than the current value;
    /// otherwise updates provenance, and raises <see cref="ExchangeRateChanged"/> only when the rate changes.
    /// </summary>
    public void Observe(
        Currency from, Currency to, decimal observedRate, DateTimeOffset observedAt, RateSource source)
    {
        var pair = CurrencyPair.Canonical(from, to);
        if (pair != Id)
        {
            throw new DomainException($"Observation {pair} does not match this rate's pair {Id}.");
        }

        if (observedAt < ObservedAt)
        {
            return;
        }

        var canonical = Normalize(pair, from, observedRate);
        var changed = canonical != Rate;

        Rate = canonical;
        ObservedAt = observedAt;
        Source = source;

        if (changed)
        {
            Raise(new ExchangeRateChanged(pair.Base, pair.Quote, canonical, observedAt, source));
        }
    }

    // Converts an observed directed rate into the canonical Base→Quote rate (reciprocal when reversed).
    private static decimal Normalize(CurrencyPair pair, Currency observedFrom, decimal observedRate)
    {
        if (observedRate <= 0m)
        {
            throw new DomainException("Exchange rate must be greater than zero.");
        }

        return observedFrom == pair.Base ? observedRate : 1m / observedRate;
    }
}
