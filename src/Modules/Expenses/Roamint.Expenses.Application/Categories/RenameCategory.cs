using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.Categories;

public sealed record RenameCategory(CategoryId CategoryId, string NewName) : ICommand;

internal sealed class RenameCategoryHandler(ICategoryRepository categories, IUnitOfWork unitOfWork)
    : ICommandHandler<RenameCategory>
{
    public async Task HandleAsync(RenameCategory command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var category = await categories.GetAsync(command.CategoryId, ct)
            ?? throw new CategoryNotFoundException(command.CategoryId);

        category.Rename(command.NewName);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
