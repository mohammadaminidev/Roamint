using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain;

public enum CategoryStatus
{
    Active = 0,
    Archived = 1,
}

/// <summary>A spending classification. Never destructively deleted — archived instead.</summary>
public sealed class Category : AggregateRoot<CategoryId>
{
    public const int MaxNameLength = 100;

    // EF / serialization constructor; not for domain use.
    private Category(CategoryId id) : base(id) { }

    private Category(CategoryId id, string name) : base(id)
    {
        Name = name;
        Status = CategoryStatus.Active;
    }

    public string Name { get; private set; } = string.Empty;

    public CategoryStatus Status { get; private set; }

    public static Category Create(CategoryId id, string name)
    {
        var clean = NormalizeName(name);
        var category = new Category(id, clean);
        category.Raise(new CategoryCreated(id, clean));
        return category;
    }

    public void Rename(string name)
    {
        var clean = NormalizeName(name);
        if (clean == Name)
        {
            return;
        }

        var previous = Name;
        Name = clean;
        Raise(new CategoryRenamed(Id, previous, clean));
    }

    public void Archive()
    {
        if (Status == CategoryStatus.Archived)
        {
            return;
        }

        Status = CategoryStatus.Archived;
        Raise(new CategoryArchived(Id));
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var clean = name.Trim();
        if (clean.Length > MaxNameLength)
        {
            throw new DomainException($"Category name must be {MaxNameLength} characters or fewer.");
        }

        return clean;
    }
}
