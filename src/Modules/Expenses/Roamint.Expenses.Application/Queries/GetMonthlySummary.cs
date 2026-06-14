using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application.Queries;

/// <summary>One bucket of spend: the total of all line amounts in a single (category × currency) pair over
/// the queried range. Lossless and unconverted — the host folds these buckets into a display-currency total
/// per category (FX is a host-side read concern, 0010/0011 D5). Category is line-level.</summary>
public sealed record CategorySpendView(
    Guid CategoryId,
    string CategoryName,
    string CurrencyCode,
    decimal Total);

/// <summary>
/// Per-(category, currency) spend sums over an inclusive date range, for the monthly-summary home (0012 D2).
/// Sums line amounts grouped by the line's category and the expense's original currency; returns the raw
/// breakdown so the host can convert and aggregate into a single display currency. Read model in
/// Infrastructure.
/// </summary>
public sealed record GetMonthlySummary(DateOnly From, DateOnly To)
    : IQuery<IReadOnlyList<CategorySpendView>>;
