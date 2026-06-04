using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain;

/// <summary>Input shape for a line item; the aggregate turns it into an owned <see cref="ExpenseLine"/>.</summary>
public sealed record ExpenseLineDraft(
    string Description,
    CategoryId CategoryId,
    Money Amount,
    Quantity? Quantity = null,
    IReadOnlySet<TagId>? TagIds = null);

/// <summary>
/// A tag applied to an <see cref="ExpenseLine"/> — a join row owned by the line. It references the
/// <see cref="Tag"/> aggregate by id only (no navigation), per the reference-other-aggregates-by-id rule;
/// scope/active compatibility is validated in the command handler that has the Tag repository.
/// </summary>
public sealed class LineTag
{
    // EF / serialization constructor.
    private LineTag() { }

    internal LineTag(TagId tagId) => TagId = tagId;

    public TagId TagId { get; private init; }
}

/// <summary>
/// A line item owned by an <see cref="Expense"/>. The item is the categorized unit: it carries its own
/// category and the amount paid for it. Quantity is optional reportable annotation, never a multiplier.
/// </summary>
public sealed class ExpenseLine : Entity<ExpenseLineId>
{
    public const int MaxDescriptionLength = 200;

    private readonly List<LineTag> _tags = [];

    // EF / serialization constructor; not for domain use. EF binds the key here and sets the mapped
    // members (which include the complex Amount, owned Quantity, and owned Tags) through their backing fields.
    private ExpenseLine(ExpenseLineId id) : base(id) { }

    internal ExpenseLine(
        ExpenseLineId id,
        string description,
        CategoryId categoryId,
        Money amount,
        Quantity? quantity,
        IReadOnlySet<TagId>? tagIds)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var clean = description.Trim();
        if (clean.Length > MaxDescriptionLength)
        {
            throw new DomainException($"Line description must be {MaxDescriptionLength} characters or fewer.");
        }

        if (!amount.IsPositive)
        {
            throw new DomainException("Line amount must be greater than zero.");
        }

        Description = clean;
        CategoryId = categoryId;
        Amount = amount;
        Quantity = quantity;
        if (tagIds is not null)
        {
            foreach (var tagId in tagIds)
            {
                _tags.Add(new LineTag(tagId));
            }
        }
    }

    public string Description { get; } = string.Empty;

    public CategoryId CategoryId { get; }

    /// <summary>The amount paid for this item, in the expense's currency.</summary>
    public Money Amount { get; }

    public Quantity? Quantity { get; }

    /// <summary>The owned join rows — the read/query path (EF navigation). Domain logic uses <see cref="TagIds"/>.</summary>
    public IReadOnlyCollection<LineTag> Tags => _tags.AsReadOnly();

    /// <summary>The tags applied to this line (0..n), as the set the domain reasons about.</summary>
    public IReadOnlySet<TagId> TagIds => _tags.Select(tag => tag.TagId).ToHashSet();
}

/// <summary>
/// A recorded payment in a single currency — a shopping trip made of one or more categorized line items.
/// The total and currency are derived from the lines; FX equivalents are a read concern driven by the
/// Wallet rate timeline and are never stored here.
/// </summary>
public sealed class Expense : AggregateRoot<ExpenseId>
{
    private readonly List<ExpenseLine> _lines = [];

    // EF / serialization constructor; not for domain use.
    private Expense(ExpenseId id) : base(id) { }

    private Expense(ExpenseId id, DateOnly occurredOn, string? note, List<ExpenseLine> lines) : base(id)
    {
        OccurredOn = occurredOn;
        Note = note;
        _lines = lines;
    }

    public DateOnly OccurredOn { get; private set; }

    public string? Note { get; private set; }

    public IReadOnlyList<ExpenseLine> Lines => _lines.AsReadOnly();

    /// <summary>The currency of the payment, inherited by every line. Derived from the lines.</summary>
    public Currency Currency => _lines[0].Amount.Currency;

    /// <summary>The total paid, derived as the sum of line amounts.</summary>
    public Money Total => _lines.Aggregate(Money.Zero(Currency), static (sum, line) => sum + line.Amount);

    public static Expense Record(
        ExpenseId id,
        DateOnly occurredOn,
        IEnumerable<ExpenseLineDraft> lines,
        TimeProvider clock,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(lines);
        EnsureNotFuture(occurredOn, clock);

        var builtLines = BuildLines(lines, clock);
        var expense = new Expense(id, occurredOn, Clean(note), builtLines);
        expense.Raise(new ExpenseRecorded(id, expense.Total, occurredOn));
        return expense;
    }

    public void Correct(
        DateOnly occurredOn,
        IEnumerable<ExpenseLineDraft> lines,
        TimeProvider clock,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(lines);
        EnsureNotFuture(occurredOn, clock);

        var builtLines = BuildLines(lines, clock);
        _lines.Clear();
        _lines.AddRange(builtLines);
        OccurredOn = occurredOn;
        Note = Clean(note);
        Raise(new ExpenseCorrected(Id, Total, occurredOn));
    }

    private static List<ExpenseLine> BuildLines(IEnumerable<ExpenseLineDraft> drafts, TimeProvider clock)
    {
        var lines = new List<ExpenseLine>();
        foreach (var draft in drafts)
        {
            lines.Add(new ExpenseLine(
                ExpenseLineId.New(clock),
                draft.Description,
                draft.CategoryId,
                draft.Amount,
                draft.Quantity,
                draft.TagIds));
        }

        if (lines.Count == 0)
        {
            throw new DomainException("An expense must have at least one line item.");
        }

        var currency = lines[0].Amount.Currency;
        foreach (var line in lines)
        {
            if (line.Amount.Currency != currency)
            {
                throw new DomainException(
                    $"Line currency {line.Amount.Currency.Code} does not match expense currency {currency.Code}.");
            }
        }

        return lines;
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
