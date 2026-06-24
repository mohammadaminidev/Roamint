using Roamint.Application.Abstractions;
using Roamint.Receipts.Application.OrderImports;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Contracts;

/// <summary>Translates the public import DTOs into the module's commands and dispatches them.</summary>
internal sealed class ImportsApi(ICommandDispatcher commands) : IImportsApi
{
    public async Task<ImportOrderResult> ImportAsync(ImportOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await commands.SendAsync(new ImportOrder(request.RawJson), ct);
        return new ImportOrderResult(
            outcome.Id.Value,
            outcome.Source,
            outcome.AlreadyImported,
            outcome.ExistingExpenseId,
            Mappings.ToDto(outcome.Draft));
    }

    public Task LinkAsync(Guid importId, Guid expenseId, CancellationToken ct = default) =>
        commands.SendAsync(new LinkImportedOrder(new ImportedOrderId(importId), expenseId), ct);
}
