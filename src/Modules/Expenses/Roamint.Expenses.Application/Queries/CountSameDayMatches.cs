using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application.Queries;

/// <summary>
/// Counts existing expenses on <paramref name="Date"/> whose currency is <paramref name="CurrencyCode"/> and
/// whose line-derived total equals <paramref name="Total"/> — the duplicate-twin signal a double-submit
/// produces (0022). A soft pre-save check, not a domain rule: equal-total same-day expenses are legal, so
/// this only feeds the UI's "save anyway?" confirmation. Read model in Infrastructure.
/// </summary>
public sealed record CountSameDayMatches(DateOnly Date, string CurrencyCode, decimal Total) : IQuery<int>;
