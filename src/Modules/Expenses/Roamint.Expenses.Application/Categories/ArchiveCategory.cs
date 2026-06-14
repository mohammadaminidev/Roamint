using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.Categories;

public sealed record ArchiveCategory(CategoryId CategoryId) : ICommand;

internal sealed class ArchiveCategoryHandler(ICategoryRepository categories, IUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveCategory>
{
    public async Task HandleAsync(ArchiveCategory command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var category = await categories.GetAsync(command.CategoryId, ct)
            ?? throw new CategoryNotFoundException(command.CategoryId);

        category.Archive();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
