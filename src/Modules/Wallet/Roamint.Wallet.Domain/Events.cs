using Roamint.SharedKernel;

namespace Roamint.Wallet.Domain;

public sealed record ExchangeRecorded(
    ExchangeId ExchangeId,
    Money From,
    Money To,
    DateOnly OccurredOn) : IDomainEvent;

public sealed record ExchangeRateChanged(
    Currency Base,
    Currency Quote,
    decimal Rate,
    DateTimeOffset ObservedAt,
    RateSource Source) : IDomainEvent;
