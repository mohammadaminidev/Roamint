namespace Roamint.Wallet.Domain;

/// <summary>Where a current <see cref="ExchangeRate"/> value came from.</summary>
public enum RateSource
{
    /// <summary>Implied by a recorded <see cref="Exchange"/>.</summary>
    Exchange = 0,

    /// <summary>Entered or overridden by the user.</summary>
    Manual = 1,
}
