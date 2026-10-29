using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Domain.Tests;

public class InboxItemTests
{
    private static readonly FakeTimeProvider Clock =
        new(DateTimeOffset.Parse("2026-10-24T09:00:00Z", CultureInfo.InvariantCulture));

    private static readonly DateTimeOffset SentOn = DateTimeOffset.Parse("2026-10-23T18:30:00Z", CultureInfo.InvariantCulture);

    private static InboxItem Received(string text = "15 هزار درام شام") =>
        InboxItem.Receive(InboxItemId.New(Clock), "telegram", "-100111:42", "777", text, SentOn, "{}", Clock);

    private static ParsedDraft OneLine() =>
        new(null, new DateOnly(2026, 10, 23), "AMD", [new ParsedLine("شام", 15000m, SourceText: "شام")]);

    [Fact]
    public void Receive_sets_received_state_and_raises_event()
    {
        var item = Received();

        item.Status.ShouldBe(InboxItemStatus.Received);
        item.Source.ShouldBe("telegram");
        item.ExternalId.ShouldBe("-100111:42");
        item.SenderExternalId.ShouldBe("777");
        item.SentOnUtc.ShouldBe(SentOn);
        item.ReceivedOnUtc.ShouldBe(Clock.GetUtcNow());
        item.Draft.ShouldBeNull();
        item.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<InboxItemReceived>().ExternalId.ShouldBe("-100111:42");
    }

    [Theory]
    [InlineData("", "-100111:42", "hi")]
    [InlineData("telegram", " ", "hi")]
    [InlineData("telegram", "-100111:42", "   ")]
    public void Receive_rejects_missing_source_id_or_text(string source, string externalId, string text) =>
        Should.Throw<DomainException>(() =>
            InboxItem.Receive(InboxItemId.New(Clock), source, externalId, null, text, SentOn, "{}", Clock));

    [Fact]
    public void Receive_truncates_over_long_text() =>
        Received(new string('x', InboxItem.MaxTextLength + 10)).Text.Length.ShouldBe(InboxItem.MaxTextLength);

    [Fact]
    public void Parse_stores_a_one_line_draft()
    {
        var item = Received();

        item.Parse(OneLine());

        item.Status.ShouldBe(InboxItemStatus.Parsed);
        item.Draft.ShouldNotBeNull().Lines.ShouldHaveSingleItem().Amount.ShouldBe(15000m);
    }

    [Fact]
    public void Parse_rejects_a_draft_with_other_than_one_line()
    {
        var two = new ParsedDraft(null, null, "AMD", [new ParsedLine("a", 1m), new ParsedLine("b", 2m)]);

        Should.Throw<DomainException>(() => Received().Parse(two));
        Should.Throw<DomainException>(() => Received().Parse(ParsedDraft.Empty));
    }

    [Fact]
    public void A_failed_item_can_be_read_again()
    {
        var item = Received();
        item.MarkFailed();

        item.Parse(OneLine());

        item.Status.ShouldBe(InboxItemStatus.Parsed);
    }

    [Fact]
    public void Settled_items_cannot_be_read_again()
    {
        var ignored = Received();
        ignored.Ignore();
        Should.Throw<DomainException>(() => ignored.Parse(OneLine()));

        var parsed = Received();
        parsed.Parse(OneLine());
        Should.Throw<DomainException>(() => parsed.Ignore());
        Should.Throw<DomainException>(() => parsed.MarkFailed());
    }

    [Fact]
    public void LinkTo_requires_a_parsed_item_and_a_real_expense()
    {
        var item = Received();
        Should.Throw<DomainException>(() => item.LinkTo(Guid.CreateVersion7()));

        item.Parse(OneLine());
        Should.Throw<DomainException>(() => item.LinkTo(Guid.Empty));

        var expenseId = Guid.CreateVersion7();
        item.ClearDomainEvents();
        item.LinkTo(expenseId);

        item.Status.ShouldBe(InboxItemStatus.Linked);
        item.LinkedExpenseId.ShouldBe(expenseId);
        item.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<InboxItemLinked>().ExpenseId.ShouldBe(expenseId);
        Should.Throw<DomainException>(() => item.LinkTo(Guid.CreateVersion7()));
    }
}
