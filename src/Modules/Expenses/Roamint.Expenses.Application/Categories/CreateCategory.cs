using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.Categories;

public sealed record CreateCategory(string Name) : ICommand<CategoryId>;

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
