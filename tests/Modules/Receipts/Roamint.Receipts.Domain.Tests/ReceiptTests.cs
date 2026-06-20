using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Domain.Tests;

public class ReceiptTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-06-19T12:00:00Z", CultureInfo.InvariantCulture));

    private static Receipt Uploaded() => Receipt.Upload(ReceiptId.New(Clock), "receipts/abc.jpg", Clock);

    private static ParsedDraft SampleDraft() => new(
        "Yerevan Market",
        new DateOnly(2026, 6, 19),
        "AMD",
        [new ParsedLine("Bread", 450m), new ParsedLine("Milk", 700m, 1m, "Pcs")]);

    [Fact]
    public void Upload_sets_uploaded_state_and_raises_event()
    {
        var id = ReceiptId.New(Clock);

        var receipt = Receipt.Upload(id, "receipts/abc.jpg", Clock);

        receipt.Status.ShouldBe(ReceiptStatus.Uploaded);
        receipt.ImageKey.ShouldBe("receipts/abc.jpg");
        receipt.UploadedOnUtc.ShouldBe(Clock.GetUtcNow());
        receipt.Draft.ShouldBeNull();
        receipt.LinkedExpenseId.ShouldBeNull();

        var ev = receipt.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ReceiptUploaded>();
        ev.ReceiptId.ShouldBe(id);
        ev.ImageKey.ShouldBe("receipts/abc.jpg");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Upload_rejects_blank_image_key(string imageKey) =>
        Should.Throw<DomainException>(() => Receipt.Upload(ReceiptId.New(Clock), imageKey, Clock));

    [Fact]
    public void Parse_stores_draft_and_transitions_to_parsed()
    {
        var receipt = Uploaded();
        var draft = SampleDraft();

        receipt.Parse(draft, Clock);

        receipt.Status.ShouldBe(ReceiptStatus.Parsed);
        receipt.Draft.ShouldBe(draft);
        receipt.ParsedOnUtc.ShouldBe(Clock.GetUtcNow());
        receipt.DomainEvents.OfType<ReceiptParsed>().ShouldHaveSingleItem().ReceiptId.ShouldBe(receipt.Id);
    }

    [Fact]
    public void Parse_rejects_a_receipt_not_in_uploaded()
    {
        var receipt = Uploaded();
        receipt.Parse(SampleDraft(), Clock);

        Should.Throw<DomainException>(() => receipt.Parse(ParsedDraft.Empty, Clock));
    }

    [Fact]
    public void MarkFailed_transitions_to_failed_and_raises_event()
    {
        var receipt = Uploaded();

        receipt.MarkFailed("vision call timed out");

        receipt.Status.ShouldBe(ReceiptStatus.Failed);
        receipt.Draft.ShouldBeNull();
        receipt.DomainEvents.OfType<ReceiptParseFailed>().ShouldHaveSingleItem().Reason.ShouldBe("vision call timed out");
    }

    [Fact]
    public void LinkTo_sets_expense_and_transitions_to_linked()
    {
        var receipt = Uploaded();
        receipt.Parse(SampleDraft(), Clock);
        var expenseId = Guid.CreateVersion7();

        receipt.LinkTo(expenseId);

        receipt.Status.ShouldBe(ReceiptStatus.Linked);
        receipt.LinkedExpenseId.ShouldBe(expenseId);
        receipt.DomainEvents.OfType<ReceiptLinked>().ShouldHaveSingleItem().ExpenseId.ShouldBe(expenseId);
    }

    [Fact]
    public void LinkTo_rejects_a_receipt_that_was_not_parsed()
    {
        var receipt = Uploaded();

        Should.Throw<DomainException>(() => receipt.LinkTo(Guid.CreateVersion7()));
    }

    [Fact]
    public void LinkTo_rejects_empty_expense_id()
    {
        var receipt = Uploaded();
        receipt.Parse(SampleDraft(), Clock);

        Should.Throw<DomainException>(() => receipt.LinkTo(Guid.Empty));
    }

    [Fact]
    public void Linked_is_terminal()
    {
        var receipt = Uploaded();
        receipt.Parse(SampleDraft(), Clock);
        receipt.LinkTo(Guid.CreateVersion7());

        Should.Throw<DomainException>(() => receipt.LinkTo(Guid.CreateVersion7()));
    }
}
