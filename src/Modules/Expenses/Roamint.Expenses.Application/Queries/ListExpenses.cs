using Roamint.Application.Abstractions;

namespace Roamint.Expenses.Application.Queries;

/// <summary>A line on an expense list row: its description, category (id + name), the amount paid, and the
/// optional quantity (value + canonical unit code, or null).</summary>
public sealed record ExpenseLineRowView(
    string Description,
    Guid CategoryId,
    string CategoryName,
    decimal Amount,
    decimal? QuantityValue,
    string? QuantityUnit);

/// <summary>An expense list row in its original currency — never converted in the module (FX is a host-side
/// read concern, 0010/0011). <c>Total</c>/<c>CurrencyCode</c> are derived from the lines.</summary>
public sealed record ExpenseRowView(
    Guid Id,
    DateOnly OccurredOn,
    string CurrencyCode,
    decimal Total,
    string? Note,
    IReadOnlyList<ExpenseLineRowView> Lines);

/// <summary>
/// Lists expenses newest-first for the list screen, optionally bounded by a date range and a line-level
/// category filter (an expense matches if any line is in the category). Currency is a display denomination,
/// not a filter (0010 D5a). Read model in Infrastructure.
/// </summary>
public sealed record ListExpenses(DateOnly? From, DateOnly? To, Guid? CategoryFilter)
    : IQuery<IReadOnlyList<ExpenseRowView>>;
