using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Application;
using Roamint.Receipts.Application.Receipts;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Tests;

public sealed class LinkReceiptTests
{
    private static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero));

    private static Receipt ParsedReceipt(TimeProvider clock)
    {
        var receipt = Receipt.Upload(ReceiptId.New(clock), "receipts/abc.jpg", clock);
        receipt.Parse(new ParsedDraft("M", null, "AMD", [new ParsedLine("x", 1m)]), clock);
        return receipt;
    }

    [Fact]
    public async Task Links_parsed_receipt_to_expense_and_saves()
    {
        var clock = NewClock();
        var receipts = new InMemoryReceiptRepository();
        var receipt = ParsedReceipt(clock);
        await receipts.AddAsync(receipt, TestContext.Current.CancellationToken);
        var uow = new FakeUnitOfWork();
        var handler = new LinkReceiptHandler(receipts, uow);
        var expenseId = Guid.CreateVersion7();

        await handler.HandleAsync(new LinkReceipt(receipt.Id, expenseId), TestContext.Current.CancellationToken);

        receipt.Status.ShouldBe(ReceiptStatus.Linked);
        receipt.LinkedExpenseId.ShouldBe(expenseId);
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Throws_when_receipt_is_unknown()
    {
        var handler = new LinkReceiptHandler(new InMemoryReceiptRepository(), new FakeUnitOfWork());

        await Should.ThrowAsync<ReceiptNotFoundException>(() =>
            handler.HandleAsync(new LinkReceipt(ReceiptId.New(NewClock()), Guid.CreateVersion7()), TestContext.Current.CancellationToken));
    }
}
