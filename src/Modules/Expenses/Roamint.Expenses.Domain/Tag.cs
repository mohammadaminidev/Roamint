using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain;

public enum TagStatus
{
    Active = 0,
    Archived = 1,
}

/// <summary>
/// A classification axis orthogonal to category. A tag is either <em>global</em> (applies under any
/// category) or <em>scoped</em> to one category (a sub-classification, e.g. "Fruit" under "Groceries").
/// A managed vocabulary, parallel to <see cref="Category"/> — created/renamed/archived, never free text,
/// never destructively deleted.
/// </summary>
public sealed class Tag : AggregateRoot<TagId>
{
    public const int MaxNameLength = 100;

    // EF / serialization constructor; not for domain use.
    private Tag(TagId id) : base(id) { }

    private Tag(TagId id, string name, CategoryId? scope) : base(id)
    {
        Name = name;
        Scope = scope;
        Status = TagStatus.Active;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The category this tag is scoped to, or null when the tag is global.</summary>
    public CategoryId? Scope { get; private set; }

    public TagStatus Status { get; private set; }

    public bool IsGlobal => Scope is null;

    public static Tag Create(TagId id, string name, CategoryId? scope = null)
    {
        var clean = NormalizeName(name);
        var tag = new Tag(id, clean, scope);
        tag.Raise(new TagCreated(id, clean, scope));
        return tag;
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
        Raise(new TagRenamed(Id, previous, clean));
    }

    public void Archive()
    {
        if (Status == TagStatus.Archived)
        {
            return;
        }

        Status = TagStatus.Archived;
        Raise(new TagArchived(Id));
    }

    /// <summary>Whether this tag may sit on a line of the given category: a global tag fits anywhere, a
    /// scoped tag only on its own category. Active/archived is a separate check the command path makes.</summary>
    public bool AppliesTo(CategoryId category) => Scope is null || Scope.Value == category;

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var clean = name.Trim();
        if (clean.Length > MaxNameLength)
        {
            throw new DomainException($"Tag name must be {MaxNameLength} characters or fewer.");
        }

        return clean;
    }
}
