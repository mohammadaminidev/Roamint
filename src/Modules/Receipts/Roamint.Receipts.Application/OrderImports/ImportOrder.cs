using System.Text.Json;
using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.OrderImports;

/// <summary>
/// Parses a pasted external-order payload into the shared <see cref="ParsedDraft"/> and lands it as an
/// <see cref="ImportedOrder"/>. Each registered <see cref="IOrderImportParser"/> is asked to recognize the
/// shape (first match wins); none-match is an <see cref="UnrecognizedImportException"/>. Re-pasting a known
/// order (same source + external id) is caught and returned as <see cref="ImportOrderOutcome.AlreadyImported"/>
/// rather than double-spent.
/// </summary>
public sealed record ImportOrder(string RawJson) : ICommand<ImportOrderOutcome>;

/// <summary>Outcome of an import: the order id, the parser that handled it, whether it was a duplicate (and
/// if so the expense it already produced), and the draft to review.</summary>
public sealed record ImportOrderOutcome(
    ImportedOrderId Id,
    string Source,
    bool AlreadyImported,
    Guid? ExistingExpenseId,
    ParsedDraft Draft);

internal sealed class ImportOrderHandler(
    IEnumerable<IOrderImportParser> parsers,
    IImportedOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ImportOrder, ImportOrderOutcome>
{
    public async Task<ImportOrderOutcome> HandleAsync(ImportOrder command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var root = ParseRoot(command.RawJson);

        var parser = parsers.FirstOrDefault(p => p.CanParse(root))
            ?? throw new UnrecognizedImportException();

        var parsed = parser.Parse(root);

        // Friendly dedup pre-check; the unique index is the backstop if two pastes race.
        var existing = await orders.FindAsync(parser.Source, parsed.ExternalOrderId, ct);
        if (existing is not null)
        {
            return new ImportOrderOutcome(
                existing.Id, existing.Source, AlreadyImported: true, existing.LinkedExpenseId, existing.Draft);
        }

        var order = ImportedOrder.Import(
            ImportedOrderId.New(clock), parser.Source, parsed.ExternalOrderId, parsed.Draft, clock);
        await orders.AddAsync(order, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new ImportOrderOutcome(order.Id, order.Source, AlreadyImported: false, null, order.Draft);
    }

    private static JsonElement ParseRoot(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new UnrecognizedImportException();
        }

        try
        {
            // Clone so the element outlives the JsonDocument (which we don't keep around).
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new UnrecognizedImportException();
        }
    }
}
