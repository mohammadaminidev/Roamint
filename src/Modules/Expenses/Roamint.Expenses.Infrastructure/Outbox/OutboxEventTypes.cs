using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Infrastructure.Outbox;

/// <summary>
/// Stable discriminator strings for the domain events the module captures. Deliberately *not* the
/// assembly-qualified type name (brittle across refactors). The drain slice resolves these back to types
/// when it learns to read the outbox; until then only the write half is exercised.
/// </summary>
internal static class OutboxEventTypes
{
    private static readonly Dictionary<Type, string> Discriminators = new()
    {
        [typeof(CategoryCreated)] = "expenses.category-created",
        [typeof(CategoryRenamed)] = "expenses.category-renamed",
        [typeof(CategoryArchived)] = "expenses.category-archived",
        [typeof(TagCreated)] = "expenses.tag-created",
        [typeof(TagRenamed)] = "expenses.tag-renamed",
        [typeof(TagArchived)] = "expenses.tag-archived",
        [typeof(ExpenseRecorded)] = "expenses.expense-recorded",
        [typeof(ExpenseCorrected)] = "expenses.expense-corrected",
    };

    public static string DiscriminatorFor(IDomainEvent domainEvent)
    {
        if (Discriminators.TryGetValue(domainEvent.GetType(), out var discriminator))
        {
            return discriminator;
        }

        throw new InvalidOperationException(
            $"No outbox discriminator registered for domain event '{domainEvent.GetType().Name}'.");
    }
}
