using Roamint.Application.Abstractions;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.Tags;

public sealed record ArchiveTag(TagId TagId) : ICommand;

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
