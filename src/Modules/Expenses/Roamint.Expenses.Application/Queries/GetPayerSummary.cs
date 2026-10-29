using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application.Queries;

/// <summary>One bucket of spend by payer: the total of all expenses one household member paid in a single
/// currency over the queried range. <see cref="PaidBy"/> null collects unattributed expenses. Unconverted —
/// the host folds the currency buckets into a display-currency total per payer, like the category summary.</summary>
public sealed record PayerSpendView(string? PaidBy, string CurrencyCode, decimal Total);

/// <summary>
/// Per-(payer, currency) spend sums over an inclusive date range, for the monthly summary's "who paid"
/// breakdown (0024). Read model in Infrastructure.
/// </summary>
public sealed record GetPayerSummary(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<PayerSpendView>>;
