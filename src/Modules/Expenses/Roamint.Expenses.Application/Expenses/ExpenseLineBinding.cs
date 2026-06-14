using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Application.Expenses;

/// <summary>Shared line-binding for the expense write use cases. Validates referenced categories and tags
/// and turns the command's <see cref="ExpenseLineInput"/> shapes into domain <see cref="ExpenseLineDraft"/>s.
/// Both <see cref="RecordExpenseHandler"/> and <see cref="CorrectExpenseHandler"/> run the same checks.</summary>
internal static class ExpenseLineBinding
{
    /// <summary>Validates every category referenced by the lines exists and is not archived. Distinct
    /// so a trip with many lines in the same category hits the repository once per category.</summary>
    public static async Task EnsureActiveCategoriesAsync(
        ICategoryRepository categories,
        IReadOnlyList<ExpenseLineInput> lines,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);

        foreach (var categoryId in lines.Select(line => line.CategoryId).Distinct())
        {
            var category = await categories.GetAsync(categoryId, ct)
                ?? throw new CategoryNotFoundException(categoryId);

            if (category.Status == CategoryStatus.Archived)
            {
                throw new DomainException($"Category '{category.Name}' is archived and cannot accept expenses.");
            }
        }
    }

    /// <summary>Validates every tag referenced by the lines exists, is active, and is scope-compatible with
    /// its line's category. Mirrors <see cref="EnsureActiveCategoriesAsync"/> and runs on both Record and
    /// Correct; the distinct referenced tags are loaded in one round trip.</summary>
    public static async Task EnsureCompatibleTagsAsync(
        ITagRepository tags,
        IReadOnlyList<ExpenseLineInput> lines,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var referenced = lines
            .SelectMany(line => line.TagIds ?? [])
            .Distinct()
            .ToList();
        if (referenced.Count == 0)
        {
            return;
        }

        var loaded = await tags.GetManyAsync(referenced, ct);
        var byId = loaded.ToDictionary(tag => tag.Id);

        foreach (var line in lines)
        {
            foreach (var tagId in line.TagIds ?? [])
            {
                if (!byId.TryGetValue(tagId, out var tag))
                {
                    throw new TagNotFoundException(tagId);
                }

                if (tag.Status == TagStatus.Archived)
                {
                    throw new DomainException($"Tag '{tag.Name}' is archived and cannot be applied.");
                }

                if (!tag.AppliesTo(line.CategoryId))
                {
                    throw new DomainException(
                        $"Tag '{tag.Name}' is scoped to a different category and cannot sit on this line.");
                }
            }
        }
    }

    public static IEnumerable<ExpenseLineDraft> ToDrafts(
        IReadOnlyList<ExpenseLineInput> lines,
        Currency currency)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Select(line => new ExpenseLineDraft(
            line.Description,
            line.CategoryId,
            Money.Of(line.Amount, currency),
            ToQuantity(line),
            line.TagIds is null ? null : line.TagIds.ToHashSet()));
    }

    private static Quantity? ToQuantity(ExpenseLineInput line)
    {
        if (line.QuantityValue is null && line.QuantityUnit is null)
        {
            return null;
        }

        if (line.QuantityValue is null || line.QuantityUnit is null)
        {
            throw new DomainException("A line quantity needs both a value and a unit, or neither.");
        }

        return Quantity.Of(line.QuantityValue.Value, line.QuantityUnit.Value);
    }
}
