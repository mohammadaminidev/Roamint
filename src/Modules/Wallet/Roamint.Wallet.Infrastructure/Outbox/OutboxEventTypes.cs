using Roamint.SharedKernel;
using Roamint.Wallet.Domain;

namespace Roamint.Wallet.Infrastructure.Outbox;

/// <summary>
/// Stable discriminator strings for the domain events the module captures — deliberately not the
/// assembly-qualified type name. The drain slice resolves these back to types once a consumer exists.
/// </summary>
internal static class OutboxEventTypes
{
    private static readonly Dictionary<Type, string> Discriminators = new()
    {
        [typeof(ExchangeRecorded)] = "wallet.exchange-recorded",
        [typeof(ExchangeRateChanged)] = "wallet.exchange-rate-changed",
    };

    public static string DiscriminatorFor(IDomainEvent domainEvent)
    {
        if (Discriminators.TryGetValue(domainEvent.GetType(), out var discriminator))
        {
            return discriminator;
        }

        throw new InvalidOperationException(
            $"No outbox discriminator registered for domain event '{domainEvent.GetType().Name}'.");
    }
}
