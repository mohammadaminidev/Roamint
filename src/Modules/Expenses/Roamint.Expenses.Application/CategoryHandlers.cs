using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application;

internal sealed class CreateCategoryHandler(ICategoryRepository categories, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateCategory, CategoryId>
{
    public async Task<CategoryId> HandleAsync(CreateCategory command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = CategoryId.New(clock);
        var category = Category.Create(id, command.Name);

        await categories.AddAsync(category, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return id;
    }
}

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
