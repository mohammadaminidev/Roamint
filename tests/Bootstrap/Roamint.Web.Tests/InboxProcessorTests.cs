using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Roamint.Expenses.Contracts;
using Roamint.Receipts.Contracts;
using Roamint.SharedKernel;
using Roamint.Web.Capture;
using Roamint.Web.Processing;
using Roamint.Web.Services;

namespace Roamint.Web.Tests;

public sealed class InboxProcessorTests
{
    private const long SaraTelegramId = 777;

    private static readonly Guid ItemId = Guid.CreateVersion7();
    private static readonly Guid DiningId = Guid.CreateVersion7();
    private static readonly Guid ExpenseId = Guid.CreateVersion7();
    private static readonly DateOnly Saturday = new(2026, 10, 24);

    private readonly IInboxApi _inbox = Substitute.For<IInboxApi>();
    private readonly IExpensesApi _expenses = Substitute.For<IExpensesApi>();
    private readonly IExpensesQueries _queries = Substitute.For<IExpensesQueries>();
    private readonly ReceiptJobs _jobs = new();

    public InboxProcessorTests()
    {
        _queries.ListActiveCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns([new CategoryListItem(DiningId, "Dining Out")]);
        _expenses.RecordExpenseAsync(Arg.Any<RecordExpenseRequest>(), Arg.Any<CancellationToken>()).Returns(ExpenseId);
    }

    private InboxProcessor Processor() => new(
        _inbox,
        _expenses,
        _queries,
        new Household(Options.Create(new HouseholdOptions
        {
            Members = { new HouseholdMember { Key = "sara", DisplayName = "Sara", TelegramUserId = SaraTelegramId } },
        })),
        _jobs,
        NullLogger<InboxProcessor>.Instance);

    private static ParsedDraftDto Draft(decimal amount = 15000m, string? currency = "AMD", string text = "شام") =>
        new(null, Saturday, currency, [new ParsedLineDto(text, amount, null, null, text)]);

    private void GivenItem(string status, long sender = SaraTelegramId, ParsedDraftDto? storedDraft = null) =>
        _inbox.GetAsync(ItemId, Arg.Any<CancellationToken>()).Returns(new InboxItemDto(
            ItemId, "telegram", sender.ToString(System.Globalization.CultureInfo.InvariantCulture), "15 هزار درام شام",
            new DateTimeOffset(2026, 10, 24, 19, 30, 0, TimeSpan.Zero), status, null, storedDraft));

    private void GivenParse(string status, ParsedDraftDto? draft = null) =>
        _inbox.ParseAsync(ItemId, Arg.Any<CancellationToken>()).Returns(new ParseInboxItemResult(status, draft));

    private void GivenLearned(string text = "شام") =>
        _queries.MatchItemAsync(text, Arg.Any<CancellationToken>()).Returns(new ItemMatchDto("شام", null, DiningId));

    private Task ProcessAsync() => Processor().ProcessAsync(ItemId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_learned_item_auto_records_with_the_payer_links_reacts_and_replies()
    {
        GivenItem("Received");
        GivenParse("Parsed", Draft());
        GivenLearned();

        await ProcessAsync();

        await _expenses.Received(1).RecordExpenseAsync(
            Arg.Is<RecordExpenseRequest>(r =>
                r.CurrencyCode == "AMD"
                && r.OccurredOn == Saturday
                && r.PaidBy == "sara"
                && r.Lines.Count == 1
                && r.Lines[0].CategoryId == DiningId
                && r.Lines[0].Amount == 15000m),
            Arg.Any<CancellationToken>());
        await _inbox.Received(1).LinkAsync(ItemId, ExpenseId, Arg.Any<CancellationToken>());
        await _inbox.Received(1).ReactAsync(ItemId, InboxReactionKind.Recorded, Arg.Any<CancellationToken>());
        await _inbox.Received(1).ReplyAsync(
            ItemId, "15,000 AMD · شام · Dining Out · paid by Sara", Arg.Any<CancellationToken>());
        _jobs.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unlearned_item_goes_to_review_with_the_payer_preselected()
    {
        GivenItem("Received");
        GivenParse("Parsed", Draft(text: "کافه"));

        await ProcessAsync();

        await _expenses.DidNotReceiveWithAnyArgs().RecordExpenseAsync(default!, TestContext.Current.CancellationToken);
        await _inbox.Received(1).ReactAsync(ItemId, InboxReactionKind.NeedsReview, Arg.Any<CancellationToken>());
        var job = _jobs.Snapshot().ShouldHaveSingleItem();
        job.Stage.ShouldBe(ReceiptJobStage.Ready);
        job.Merchant.ShouldBe("کافه");
        var seed = job.Seed.ShouldNotBeNull();
        seed.Source.ShouldBe(CaptureSource.Inbox);
        seed.SourceId.ShouldBe(ItemId);
        seed.PaidBy.ShouldBe("sara");
        seed.Date.ShouldBe(Saturday);
        var line = seed.Lines.ShouldHaveSingleItem();
        line.SourceText.ShouldBe("کافه");
        line.CategoryId.ShouldBeNull();
    }

    [Theory]
    [InlineData(15000, null)] // no currency stated
    [InlineData(15000, "IRT")] // not transactable
    [InlineData(0, "AMD")] // no readable amount
    public async Task A_learned_item_with_a_guessed_value_still_goes_to_review(int amount, string? currency)
    {
        GivenItem("Received");
        GivenParse("Parsed", Draft(amount, currency));
        GivenLearned();

        await ProcessAsync();

        await _expenses.DidNotReceiveWithAnyArgs().RecordExpenseAsync(default!, TestContext.Current.CancellationToken);
        _jobs.Snapshot().ShouldHaveSingleItem().Seed!.Lines[0].CategoryId.ShouldBe(DiningId);
    }

    [Fact]
    public async Task A_same_day_twin_goes_to_review_instead_of_double_recording()
    {
        GivenItem("Received");
        GivenParse("Parsed", Draft());
        GivenLearned();
        _queries.CountSameDayMatchesAsync(Saturday, "AMD", 15000m, Arg.Any<CancellationToken>()).Returns(1);

        await ProcessAsync();

        await _expenses.DidNotReceiveWithAnyArgs().RecordExpenseAsync(default!, TestContext.Current.CancellationToken);
        await _inbox.Received(1).ReactAsync(ItemId, InboxReactionKind.NeedsReview, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rejected_auto_record_falls_back_to_review()
    {
        GivenItem("Received");
        GivenParse("Parsed", Draft());
        GivenLearned();
        _expenses.RecordExpenseAsync(Arg.Any<RecordExpenseRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException("Category is archived."));

        await ProcessAsync();

        await _inbox.DidNotReceiveWithAnyArgs().LinkAsync(default, default, TestContext.Current.CancellationToken);
        _jobs.Snapshot().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_unknown_sender_is_ignored_without_reading_the_message()
    {
        GivenItem("Received", sender: 999);

        await ProcessAsync();

        await _inbox.Received(1).IgnoreAsync(ItemId, Arg.Any<CancellationToken>());
        await _inbox.DidNotReceiveWithAnyArgs().ParseAsync(default, TestContext.Current.CancellationToken);
        await _inbox.Received(1).ReactAsync(ItemId, InboxReactionKind.None, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Ignored", InboxReactionKind.None)]
    [InlineData("Failed", InboxReactionKind.Failed)]
    public async Task Chatter_clears_the_reaction_and_a_failed_read_shows_it(string outcome, InboxReactionKind reaction)
    {
        GivenItem("Received");
        GivenParse(outcome);

        await ProcessAsync();

        await _inbox.Received(1).ReactAsync(ItemId, reaction, Arg.Any<CancellationToken>());
        await _expenses.DidNotReceiveWithAnyArgs().RecordExpenseAsync(default!, TestContext.Current.CancellationToken);
        _jobs.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_parsed_item_from_the_sweep_is_re_evaluated_without_reading_it_again()
    {
        GivenItem("Parsed", storedDraft: Draft());
        GivenLearned(); // learned since the item first went to review

        await ProcessAsync();

        await _inbox.DidNotReceiveWithAnyArgs().ParseAsync(default, TestContext.Current.CancellationToken);
        await _inbox.Received(1).LinkAsync(ItemId, ExpenseId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_a_configured_household_items_are_left_unsettled_not_ignored()
    {
        GivenItem("Received");
        var processor = new InboxProcessor(
            _inbox, _expenses, _queries, new Household(Options.Create(new HouseholdOptions())), _jobs,
            NullLogger<InboxProcessor>.Instance);

        await processor.ProcessAsync(ItemId, TestContext.Current.CancellationToken);

        await _inbox.DidNotReceiveWithAnyArgs().IgnoreAsync(default, TestContext.Current.CancellationToken);
        await _inbox.DidNotReceiveWithAnyArgs().ParseAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_settled_item_is_left_alone()
    {
        GivenItem("Linked");

        await ProcessAsync();

        await _inbox.DidNotReceiveWithAnyArgs().ParseAsync(default, TestContext.Current.CancellationToken);
        await _inbox.DidNotReceiveWithAnyArgs().ReactAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Failed_feedback_does_not_undo_or_break_a_recorded_expense()
    {
        GivenItem("Received");
        GivenParse("Parsed", Draft());
        GivenLearned();
        _inbox.ReactAsync(ItemId, Arg.Any<InboxReactionKind>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("telegram down"));

        await ProcessAsync();

        await _inbox.Received(1).LinkAsync(ItemId, ExpenseId, Arg.Any<CancellationToken>());
        await _inbox.Received(1).ReplyAsync(ItemId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
