using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Contracts;

/// <summary>Maps the Application read views and shared draft value object to the module's public DTOs.</summary>
internal static class Mappings
{
    public static ParsedDraftDto? ToDto(DraftView? draft) =>
        draft is null
            ? null
            : new ParsedDraftDto(
                draft.Merchant,
                draft.Date,
                draft.CurrencyCode,
                draft.Lines
                    .Select(line => new ParsedLineDto(
                        line.Description, line.Amount, line.QuantityValue, line.QuantityUnit, line.SourceText))
                    .ToList());

    // The import command returns the domain ParsedDraft directly (never via a read model), so map that too.
    public static ParsedDraftDto ToDto(ParsedDraft draft) =>
        new(draft.Merchant,
            draft.Date,
            draft.CurrencyCode,
            draft.Lines
                .Select(line => new ParsedLineDto(
                    line.Description, line.Amount, line.QuantityValue, line.QuantityUnit, line.SourceText))
                .ToList());
}
