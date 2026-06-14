using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Application.Tags;

/// <summary>Creates a tag. <paramref name="Scope"/> null = global; non-null = scoped to that category.
/// Idempotent on the active (name, scope) pair — a collision returns the existing tag's id.</summary>
public sealed record CreateTag(string Name, CategoryId? Scope) : ICommand<TagId>;

internal sealed class CreateTagHandler(ITagRepository tags, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateTag, TagId>
{
    public async Task<TagId> HandleAsync(CreateTag command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Idempotent on the active (name, scope) pair: a collision returns the existing tag rather than
        // erroring, so the inline-create path (D5) just selects it. Archived same-name tags don't block a
        // fresh active one — they stay archived and out of the picker.
        var clean = command.Name?.Trim() ?? string.Empty;
        var existing = await tags.FindActiveByNameAsync(clean, command.Scope, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var id = TagId.New(clock);
        var tag = Tag.Create(id, command.Name!, command.Scope);

        await tags.AddAsync(tag, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return id;
    }
}
