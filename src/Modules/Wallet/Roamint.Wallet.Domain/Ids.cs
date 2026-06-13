namespace Roamint.Wallet.Domain;

/// <summary>Identity of an <see cref="Exchange"/> aggregate.</summary>
public readonly record struct ExchangeId(Guid Value)
{
    public static ExchangeId New(TimeProvider clock) => new(Guid.CreateVersion7(clock.GetUtcNow()));
}
