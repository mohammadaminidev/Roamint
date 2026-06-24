using System.Text.Json;
using Roamint.Receipts.Infrastructure.Parsers;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Infrastructure.Tests;

public sealed class YerevanCityOrderParserTests
{
    private static readonly YerevanCityOrderParser Parser = new();

    private static JsonElement Root(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public void CanParse_recognizes_a_yerevan_city_payload() =>
        Parser.CanParse(Root(YerevanCitySample.Json)).ShouldBeTrue();

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"data":{"orderItems":[]}}""")]
    [InlineData("""{"orders":[{"total":5}]}""")]
    [InlineData("""{"success":true,"data":{"orderItems":[{"name":"x","price":1}]}}""")]
    public void CanParse_rejects_other_shapes(string json) =>
        Parser.CanParse(Root(json)).ShouldBeFalse();

    [Fact]
    public void Parse_maps_the_sample_to_a_draft()
    {
        var order = Parser.Parse(Root(YerevanCitySample.Json));

        order.ExternalOrderId.ShouldBe("AC0000122700");

        var draft = order.Draft;
        draft.Merchant.ShouldBe("Yerevan City");
        draft.CurrencyCode.ShouldBe("AMD");
        draft.Date.ShouldBe(new DateOnly(2026, 6, 21));
        draft.Lines.Count.ShouldBe(10);

        // Per-line price is the line total; the whole order reconciles to totalPrice (6787, ±rounding).
        draft.Lines.Sum(line => line.Amount).ShouldBe(6787.04m);

        var cherry = draft.Lines.Single(line => line.Description == "Cherry");
        cherry.QuantityValue.ShouldBe(722m); // 0.722 kg → grams
        cherry.QuantityUnit.ShouldBe("g");

        var eggs = draft.Lines.Single(line => line.Description == "Eggs");
        eggs.QuantityValue.ShouldBe(1m);
        eggs.QuantityUnit.ShouldBe("Pcs");

        var patties = draft.Lines.Single(line => line.Description == "Patties");
        patties.QuantityValue.ShouldBe(2m); // line price is the total for 2, not unit price
        patties.QuantityUnit.ShouldBe("Pcs");

        // The stable English name doubles as the 0016 match key.
        eggs.SourceText.ShouldBe("Eggs");
    }

    [Fact]
    public void Parse_rejects_a_payload_whose_lines_do_not_reconcile()
    {
        // totalPrice claims 9999 but the lines sum to ~6787 — beyond tolerance.
        var tampered = YerevanCitySample.Json.Replace("\"totalPrice\": 6787", "\"totalPrice\": 9999");

        Should.Throw<DomainException>(() => Parser.Parse(Root(tampered)));
    }
}
