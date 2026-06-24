using System.Globalization;
using System.Text.Json;
using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Infrastructure.Parsers;

/// <summary>
/// Maps a Yerevan City online-order payload (the JSON the shop's app returns for an order) into a
/// <see cref="ParsedDraft"/>. A pure function of the pasted text — no token, no network. Recognizes the shape
/// by its structural fingerprint (a <c>success</c> flag plus a <c>data.orderItems</c> array whose items carry
/// <c>codeSap</c>/<c>isKilogram</c>), then maps per the 0017 field table. The per-line <c>price</c> is the
/// line total (already AMD), so no qty×unit reconstruction; the parser asserts the line totals reconcile to
/// <c>data.totalPrice</c> within a rounding tolerance and rejects a payload that doesn't.
/// </summary>
internal sealed class YerevanCityOrderParser : IOrderImportParser
{
    private const string MerchantName = "Yerevan City";
    private const string Currency = "AMD";

    public string Source => "YerevanCity";

    public bool CanParse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("success", out _)
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("orderItems", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        // An empty array is structurally recognizable but useless; require the fingerprint fields on a line.
        foreach (var item in items.EnumerateArray())
        {
            return item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("codeSap", out _)
                && item.TryGetProperty("isKilogram", out _);
        }

        return false;
    }

    public ParsedOrder Parse(JsonElement root)
    {
        var data = root.GetProperty("data");

        var externalOrderId = data.TryGetProperty("orderId", out var orderId) ? orderId.GetString() : null;
        if (string.IsNullOrWhiteSpace(externalOrderId))
        {
            throw new DomainException("This Yerevan City order has no order id.");
        }

        var date = ReadDate(data);

        var lines = new List<ParsedLine>();
        var lineSum = 0m;
        foreach (var item in data.GetProperty("orderItems").EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
            var price = ReadDecimal(item, "price");
            var quantity = ReadDecimal(item, "quantity");
            var isKilogram = item.TryGetProperty("isKilogram", out var kg) && kg.ValueKind == JsonValueKind.True;

            lineSum += price;

            var (qtyValue, qtyUnit) = ReadQuantity(quantity, isKilogram);

            // name is the stable English key 0016 matches on; it also seeds the first-sight description, which
            // the user renames to Farsi once (then it's learned).
            lines.Add(new ParsedLine(name, price, qtyValue, qtyUnit, name));
        }

        if (lines.Count == 0)
        {
            throw new DomainException("This Yerevan City order has no items.");
        }

        AssertReconciles(lineSum, data);

        return new ParsedOrder(externalOrderId, new ParsedDraft(MerchantName, date, Currency, lines));
    }

    // The line totals must reconcile to data.totalPrice (the gross of item prices, before bonus/delivery).
    // Validate against totalPrice, not totalToPay, so a redeemed-bonus order still reconciles (0017 open Q).
    private static void AssertReconciles(decimal lineSum, JsonElement data)
    {
        if (!data.TryGetProperty("totalPrice", out var totalEl) || totalEl.ValueKind != JsonValueKind.Number)
        {
            return; // No declared total to check against — trust the lines.
        }

        var total = totalEl.GetDecimal();
        var tolerance = Math.Max(1m, total * 0.01m); // rounding slack: 1 AMD or 1% of the order, whichever is larger.
        if (Math.Abs(lineSum - total) > tolerance)
        {
            throw new DomainException(
                $"This order's item prices sum to {lineSum:0.##} AMD but the order total is {total:0.##} AMD; "
                + "the payload looks incomplete.");
        }
    }

    // Weight items: the ordered weight is in kilograms — convert to whole grams (the review unit). Count
    // items: the quantity is a piece count. The package size (weightMeasure) is product metadata, not the
    // purchase unit, so it is ignored. A non-positive quantity yields no unit (the user fills it in).
    private static (decimal? Value, string? Unit) ReadQuantity(decimal quantity, bool isKilogram)
    {
        if (isKilogram)
        {
            var grams = Math.Round(quantity * 1000m, 3, MidpointRounding.AwayFromZero);
            return grams > 0m ? (grams, "g") : (null, null);
        }

        return quantity > 0m ? (quantity, "Pcs") : (null, null);
    }

    private static DateOnly? ReadDate(JsonElement data)
    {
        if (!data.TryGetProperty("createDate", out var createDate) || createDate.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = createDate.GetString();
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateOnly.FromDateTime(parsed.UtcDateTime)
            : null;
    }

    private static decimal ReadDecimal(JsonElement item, string name) =>
        item.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
            ? element.GetDecimal()
            : 0m;
}
