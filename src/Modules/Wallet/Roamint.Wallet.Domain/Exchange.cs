using Roamint.SharedKernel;

namespace Roamint.Wallet.Domain;

/// <summary>
/// A recorded currency swap — the user gave <see cref="From"/> and received <see cref="To"/> on
/// <see cref="OccurredOn"/>. The implied rate feeds the current <see cref="ExchangeRate"/> for the pair;
/// later slices also apply the swap to per-currency wallet balances.
/// </summary>
public sealed class Exchange : AggregateRoot<ExchangeId>
{
    // EF / serialization constructor; not for domain use. The factory sets state via the private setters
    // (EF materializes the complex Money columns through the backing fields), so there is a single,
    // unambiguous constructor for EF to bind — see 0010 D-EF.
    private Exchange(ExchangeId id) : base(id) { }

    /// <summary>What was given up.</summary>
    public Money From { get; private set; }

    /// <summary>What was received.</summary>
    public Money To { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    /// <summary>Implied rate for direction <c>From.Currency → To.Currency</c> (one from-unit = rate to-units).</summary>
    public decimal ImpliedRate => To.Amount / From.Amount;

    public static Exchange Record(ExchangeId id, Money from, Money to, DateOnly occurredOn, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (from.Currency == to.Currency)
        {
            throw new DomainException("An exchange must convert between two different currencies.");
        }

        if (!from.IsPositive || !to.IsPositive)
        {
            throw new DomainException("Exchange amounts must be greater than zero.");
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (occurredOn > today)
        {
            throw new DomainException("Exchange cannot occur in the future.");
        }

        var exchange = new Exchange(id) { From = from, To = to, OccurredOn = occurredOn };
        exchange.Raise(new ExchangeRecorded(id, from, to, occurredOn));
        return exchange;
    }
}
