namespace Roamint.Receipts.Infrastructure.Tests;

public sealed class InboxExtractorParseTests
{
    [Fact]
    public void Reads_the_json_object_out_of_a_fenced_reply()
    {
        var extraction = ChatClientInboxExtractor.Parse(
            """
            ```json
            {"isExpense": true, "amount": 15000, "currencyCode": "AMD", "description": "شام", "date": "2026-10-24"}
            ```
            """);

        extraction.IsExpense.ShouldBeTrue();
        extraction.Amount.ShouldBe(15000m);
        extraction.CurrencyCode.ShouldBe("AMD");
        extraction.Description.ShouldBe("شام");
        extraction.Date.ShouldBe(new DateOnly(2026, 10, 24));
    }

    [Fact]
    public void Nulls_and_an_unparseable_date_come_back_as_null()
    {
        var extraction = ChatClientInboxExtractor.Parse(
            """{"isExpense": false, "amount": null, "currencyCode": null, "description": null, "date": "tonight"}""");

        extraction.IsExpense.ShouldBeFalse();
        extraction.Amount.ShouldBeNull();
        extraction.Date.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sorry, I can't help with that.")]
    public void A_reply_without_json_throws_so_the_item_fails_rather_than_being_dropped(string? reply) =>
        Should.Throw<InvalidOperationException>(() => ChatClientInboxExtractor.Parse(reply));
}
