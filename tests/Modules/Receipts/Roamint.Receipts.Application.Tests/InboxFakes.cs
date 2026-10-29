using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Tests;

internal sealed class InMemoryInboxItemRepository : IInboxItemRepository
{
    private readonly List<InboxItem> _store = [];

    public Task<InboxItem?> GetAsync(InboxItemId id, CancellationToken ct) =>
        Task.FromResult(_store.FirstOrDefault(item => item.Id == id));

    public Task<InboxItem?> FindAsync(string source, string externalId, CancellationToken ct) =>
        Task.FromResult(_store.FirstOrDefault(item => item.Source == source && item.ExternalId == externalId));

    public Task AddAsync(InboxItem item, CancellationToken ct)
    {
        _store.Add(item);
        return Task.CompletedTask;
    }

    public IReadOnlyList<InboxItem> All => _store;
}

/// <summary>Returns a fixed extraction and records the date it was anchored to.</summary>
internal sealed class StubExtractor(InboxExtraction extraction) : IInboxExtractor
{
    public DateOnly? SentOn { get; private set; }

    public Task<InboxExtraction> ExtractAsync(string text, DateOnly sentOn, CancellationToken ct)
    {
        SentOn = sentOn;
        return Task.FromResult(extraction);
    }
}

internal sealed class ThrowingExtractor : IInboxExtractor
{
    public Task<InboxExtraction> ExtractAsync(string text, DateOnly sentOn, CancellationToken ct) =>
        throw new InvalidOperationException("LLM down");
}

internal sealed class RecordingNotifier : IInboxNotifier
{
    public List<(InboxItemId Id, InboxReaction Reaction)> Reactions { get; } = [];

    public List<(InboxItemId Id, string Text)> Replies { get; } = [];

    public Task ReactAsync(InboxItem item, InboxReaction reaction, CancellationToken ct)
    {
        Reactions.Add((item.Id, reaction));
        return Task.CompletedTask;
    }

    public Task ReplyAsync(InboxItem item, string text, CancellationToken ct)
    {
        Replies.Add((item.Id, text));
        return Task.CompletedTask;
    }
}
