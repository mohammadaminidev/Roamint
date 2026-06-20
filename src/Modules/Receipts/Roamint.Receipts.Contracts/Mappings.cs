using Roamint.Receipts.Application;

namespace Roamint.Receipts.Contracts;

/// <summary>Maps the Application read views to the module's public DTOs.</summary>
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
                    .Select(line => new ParsedLineDto(line.Description, line.Amount, line.QuantityValue, line.QuantityUnit))
                    .ToList());
}
