using NSubstitute;
using Roamint.Expenses.Contracts;
using Roamint.Receipts.Contracts;
using Roamint.Web.Capture;

namespace Roamint.Web.Tests;

public sealed class InboxReviewSeedTests
{
    private static readonly Guid ItemId = Guid.CreateVersion7();
    private static readonly Guid DiningId = Guid.CreateVersion7();
    private static readonly DateOnly Saturday = new(2026, 10, 24);
    private static readonly DateTimeOffset SentOn = new(2026, 10, 24, 19, 30, 0, TimeSpan.Zero);

    private readonly IExpensesQueries _queries = Substitute.For<IExpensesQueries>();

    private static InboxItemDto Item(ParsedDraftDto? draft) =>
        new(ItemId, "telegram", "777", "5 هزار تا برای شام", SentOn, "Parsed", null, draft);

    private static ParsedDraftDto Draft(string text, DateOnly? date = null) =>
        new(null, date, "AMD", [new ParsedLineDto(text, 5000m, null, null, text)]);

    private Task<CaptureSeed?> BuildAsync(InboxItemDto item) =>
        InboxReviewSeed.BuildAsync(item, "sara", _queries, TestContext.Current.CancellationToken);

    [Fact]
    public async Task An_unlearned_line_keeps_the_extracted_text_and_leaves_the_category_open()
    {
        var seed = (await BuildAsync(Item(Draft("کافه", Saturday)))).ShouldNotBeNull();

        seed.Source.ShouldBe(CaptureSource.Inbox);
        seed.SourceId.ShouldBe(ItemId);
        seed.PaidBy.ShouldBe("sara");
        seed.CurrencyCode.ShouldBe("AMD");
        seed.Date.ShouldBe(Saturday);
        var line = seed.Lines.ShouldHaveSingleItem();
        line.SourceText.ShouldBe("کافه");
        line.Description.ShouldBe("کافه");
        line.Amount.ShouldBe(5000m);
        line.CategoryId.ShouldBeNull();
    }

    [Fact]
    public async Task A_learned_line_pins_the_canonical_name_category_and_unit()
    {
        _queries.MatchItemAsync("شام", Arg.Any<CancellationToken>()).Returns(new ItemMatchDto("شام بیرون", "pcs", DiningId));

        var line = (await BuildAsync(Item(Draft("شام", Saturday)))).ShouldNotBeNull().Lines.ShouldHaveSingleItem();

        line.Description.ShouldBe("شام بیرون");
        line.CategoryId.ShouldBe(DiningId);
        line.Unit.ShouldBe("pcs");
        line.SourceText.ShouldBe("شام");
    }

    [Fact]
    public async Task Without_a_draft_date_the_sent_day_is_used_and_without_a_draft_there_is_nothing_to_review()
    {
        (await BuildAsync(Item(Draft("شام")))).ShouldNotBeNull().Date.ShouldBe(Saturday);
        (await BuildAsync(Item(null))).ShouldBeNull();
    }
}
