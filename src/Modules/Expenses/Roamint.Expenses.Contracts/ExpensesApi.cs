using Roamint.Application.Abstractions;
using Roamint.Expenses.Application.Categories;
using Roamint.Expenses.Application.Expenses;
using Roamint.Expenses.Application.ItemMappings;
using Roamint.Expenses.Application.Tags;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Contracts;

/// <summary>
/// Translates primitive request DTOs into the module's strongly-typed commands and dispatches them. This
/// is where a primitive <see cref="Guid"/>/currency-code crosses into the module's internal vocabulary.
/// </summary>
internal sealed class ExpensesApi(ICommandDispatcher commands) : IExpensesApi
{
    public async Task<Guid> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = await commands.SendAsync(new CreateCategory(request.Name), ct);
        return id.Value;
    }

    public Task RenameCategoryAsync(Guid categoryId, string newName, CancellationToken ct = default) =>
        commands.SendAsync(new RenameCategory(new CategoryId(categoryId), newName), ct);

    public Task ArchiveCategoryAsync(Guid categoryId, CancellationToken ct = default) =>
        commands.SendAsync(new ArchiveCategory(new CategoryId(categoryId)), ct);

    public async Task<Guid> CreateTagAsync(CreateTagRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = request.Scope is Guid value ? new CategoryId(value) : (CategoryId?)null;
        var id = await commands.SendAsync(new CreateTag(request.Name, scope), ct);
        return id.Value;
    }

    public Task RenameTagAsync(Guid tagId, string newName, CancellationToken ct = default) =>
        commands.SendAsync(new RenameTag(new TagId(tagId), newName), ct);

    public Task ArchiveTagAsync(Guid tagId, CancellationToken ct = default) =>
        commands.SendAsync(new ArchiveTag(new TagId(tagId)), ct);

    public async Task<Guid> RecordExpenseAsync(RecordExpenseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = await commands.SendAsync(
            new RecordExpense(
                request.CurrencyCode,
                request.OccurredOn,
                ToLines(request.Lines),
                request.Note),
            ct);
        return id.Value;
    }

    public Task CorrectExpenseAsync(Guid expenseId, CorrectExpenseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return commands.SendAsync(
            new CorrectExpense(
                new ExpenseId(expenseId),
                request.CurrencyCode,
                request.OccurredOn,
                ToLines(request.Lines),
                request.Note),
            ct);
    }

    public Task RecordItemMappingAsync(RecordItemMappingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return commands.SendAsync(
            new RecordItemMapping(
                request.SourceText,
                request.CanonicalDescription,
                request.Unit,
                new CategoryId(request.CategoryId)),
            ct);
    }

    private static List<ExpenseLineInput> ToLines(IReadOnlyList<ExpenseLineDto> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(line => new ExpenseLineInput(
            line.Description,
            new CategoryId(line.CategoryId),
            line.Amount,
            line.QuantityValue,
            line.QuantityUnit is null ? null : QuantityUnits.FromCode(line.QuantityUnit),
            line.TagIds?.Select(id => new TagId(id)).ToList())).ToList();
    }
}
