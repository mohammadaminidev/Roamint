using System.Text.Json;
using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Tests;

internal sealed class InMemoryImportedOrderRepository : IImportedOrderRepository
{
    private readonly List<ImportedOrder> _store = [];

    public Task<ImportedOrder?> GetAsync(ImportedOrderId id, CancellationToken ct) =>
        Task.FromResult<ImportedOrder?>(_store.FirstOrDefault(order => order.Id == id));

    public Task<ImportedOrder?> FindAsync(string source, string externalOrderId, CancellationToken ct) =>
        Task.FromResult<ImportedOrder?>(
            _store.FirstOrDefault(order => order.Source == source && order.ExternalOrderId == externalOrderId));

    public Task AddAsync(ImportedOrder order, CancellationToken ct)
    {
        _store.Add(order);
        return Task.CompletedTask;
    }

    public IReadOnlyList<ImportedOrder> All => _store;
}

/// <summary>A configurable parser stub — recognizes payloads and maps them per the supplied delegates.</summary>
internal sealed class FakeOrderParser(
    string source,
    Func<JsonElement, bool> canParse,
    Func<JsonElement, ParsedOrder> parse) : IOrderImportParser
{
    public string Source => source;

    public bool CanParse(JsonElement root) => canParse(root);

    public ParsedOrder Parse(JsonElement root) => parse(root);
}
