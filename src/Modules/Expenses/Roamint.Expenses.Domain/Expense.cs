using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain;

/// <summary>Input shape for a line item; the aggregate turns it into an owned <see cref="ExpenseLine"/>.</summary>
public sealed record ExpenseLineDraft(string Description, decimal Quantity, Money UnitPrice);

/// <summary>A line item owned by an <see cref="Expense"/>.</summary>
public sealed class ExpenseLine : Entity<ExpenseLineId>
{
    public const int MaxDescriptionLength = 200;

    internal ExpenseLine(ExpenseLineId id, string description, decimal quantity, Money unitPrice) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var clean = description.Trim();
        if (clean.Length > MaxDescriptionLength)
        {
            throw new DomainException($"Line description must be {MaxDescriptionLength} characters or fewer.");
        }

        if (quantity <= 0m)
        {
            throw new DomainException("Line quantity must be greater than zero.");
        }

        if (!unitPrice.IsPositive)
        {
            throw new DomainException("Line unit price must be greater than zero.");
        }

        Description = clean;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string Description { get; }

    public decimal Quantity { get; }

    public Money UnitPrice { get; }

    public Money LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// A recorded expense in a single currency. FX equivalents are a read concern driven by the Wallet rate
/// timeline and are never stored here.
/// </summary>
public sealed class Expense : AggregateRoot<ExpenseId>
{
    private readonly List<ExpenseLine> _lines = [];

    // EF / serialization constructor; not for domain use.
    private Expense(ExpenseId id) : base(id) { }

    private Expense(
        ExpenseId id,
        CategoryId categoryId,
        Money amount,
        DateOnly occurredOn,
        string? note,
        List<ExpenseLine> lines) : base(id)
    {
        CategoryId = categoryId;
        Amount = amount;
        OccurredOn = occurredOn;
        Note = note;
        _lines = lines;
    }

    public CategoryId CategoryId { get; private set; }

    public Money Amount { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public string? Note { get; private set; }

    public IReadOnlyList<ExpenseLine> Lines => _lines.AsReadOnly();

    public static Expense Record(
        ExpenseId id,
        CategoryId categoryId,
        Money amount,
        DateOnly occurredOn,
        TimeProvider clock,
        string? note = null,
        IEnumerable<ExpenseLineDraft>? lines = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsurePositive(amount);
        EnsureNotFuture(occurredOn, clock);

        var builtLines = BuildLines(lines, amount, clock);
        var expense = new Expense(id, categoryId, amount, occurredOn, Clean(note), builtLines);
        expense.Raise(new ExpenseRecorded(id, categoryId, amount, occurredOn));
        return expense;
    }

    public void Recategorize(CategoryId categoryId)
    {
        if (categoryId == CategoryId)
        {
            return;
        }

        var previous = CategoryId;
        CategoryId = categoryId;
        Raise(new ExpenseRecategorized(Id, previous, categoryId));
    }

    public void Correct(
        Money amount,
        DateOnly occurredOn,
        TimeProvider clock,
        string? note = null,
        IEnumerable<ExpenseLineDraft>? lines = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsurePositive(amount);
        EnsureNotFuture(occurredOn, clock);

        var builtLines = BuildLines(lines, amount, clock);
        Amount = amount;
        OccurredOn = occurredOn;
        Note = Clean(note);
        _lines.Clear();
        _lines.AddRange(builtLines);
        Raise(new ExpenseCorrected(Id, amount, occurredOn));
    }

    private static List<ExpenseLine> BuildLines(
        IEnumerable<ExpenseLineDraft>? drafts,
        Money amount,
        TimeProvider clock)
    {
        var lines = new List<ExpenseLine>();
        if (drafts is null)
        {
            return lines;
        }

        foreach (var draft in drafts)
        {
            if (draft.UnitPrice.Currency != amount.Currency)
            {
                throw new DomainException(
                    $"Line currency {draft.UnitPrice.Currency.Code} does not match expense currency {amount.Currency.Code}.");
            }

            lines.Add(new ExpenseLine(ExpenseLineId.New(clock), draft.Description, draft.Quantity, draft.UnitPrice));
        }

        if (lines.Count == 0)
        {
            return lines;
        }

        var total = lines.Aggregate(Money.Zero(amount.Currency), (sum, line) => sum + line.LineTotal);
        if (total != amount)
        {
            throw new DomainException(
                $"Line items total {total} does not equal the expense amount {amount}.");
        }

        return lines;
    }

    private static void EnsurePositive(Money amount)
    {
        if (!amount.IsPositive)
        {
            throw new DomainException("Expense amount must be greater than zero.");
        }
    }

    private static void EnsureNotFuture(DateOnly occurredOn, TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (occurredOn > today)
        {
            throw new DomainException("Expense cannot occur in the future.");
        }
    }

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
