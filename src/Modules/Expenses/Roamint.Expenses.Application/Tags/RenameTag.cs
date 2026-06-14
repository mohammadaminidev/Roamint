using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Application.Tags;

public sealed record RenameTag(TagId TagId, string NewName) : ICommand;

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
