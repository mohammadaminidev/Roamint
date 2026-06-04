using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Application;

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

internal sealed class RenameTagHandler(ITagRepository tags, IUnitOfWork unitOfWork)
    : ICommandHandler<RenameTag>
{
    public async Task HandleAsync(RenameTag command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tag = await tags.GetAsync(command.TagId, ct)
            ?? throw new TagNotFoundException(command.TagId);

        var clean = command.NewName?.Trim() ?? string.Empty;
        var clash = await tags.FindActiveByNameAsync(clean, tag.Scope, ct);
        if (clash is not null && clash.Id != tag.Id)
        {
            throw new DomainException($"A tag named '{clean}' already exists in this scope.");
        }

        tag.Rename(command.NewName!);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

internal sealed class ArchiveTagHandler(ITagRepository tags, IUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveTag>
{
    public async Task HandleAsync(ArchiveTag command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tag = await tags.GetAsync(command.TagId, ct)
            ?? throw new TagNotFoundException(command.TagId);

        tag.Archive();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
