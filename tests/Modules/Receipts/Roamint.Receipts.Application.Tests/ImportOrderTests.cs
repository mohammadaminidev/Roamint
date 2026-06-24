using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Application;
using Roamint.Receipts.Application.OrderImports;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Tests;

public sealed class ImportOrderTests
{
    private static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

    // Recognizes any object carrying an "orderId" string; maps it to a one-line draft.
    private static FakeOrderParser Parser() => new(
        "TestShop",
        root => root.TryGetProperty("orderId", out var id) && id.ValueKind == JsonValueKind.String,
        root => new ParsedOrder(
            root.GetProperty("orderId").GetString()!,
            new ParsedDraft("Test Shop", new DateOnly(2026, 6, 21), "AMD",
                [new ParsedLine("Item", 100m, null, null, "Item")])));

    private static ImportOrderHandler Handler(InMemoryImportedOrderRepository repo, FakeUnitOfWork uow) =>
        new([Parser()], repo, uow, NewClock());

    [Fact]
    public async Task Imports_a_recognized_order_and_saves_once()
    {
        var repo = new InMemoryImportedOrderRepository();
        var uow = new FakeUnitOfWork();
        var handler = Handler(repo, uow);

        var outcome = await handler.HandleAsync(
            new ImportOrder("""{"orderId":"AC0000122700"}"""), TestContext.Current.CancellationToken);

        outcome.AlreadyImported.ShouldBeFalse();
        outcome.Source.ShouldBe("TestShop");
        outcome.ExistingExpenseId.ShouldBeNull();
        outcome.Draft.Lines.ShouldHaveSingleItem().Description.ShouldBe("Item");
        uow.SaveCount.ShouldBe(1);

        var stored = repo.All.ShouldHaveSingleItem();
        stored.ExternalOrderId.ShouldBe("AC0000122700");
        stored.Status.ShouldBe(ImportedOrderStatus.Imported);
    }

    [Fact]
    public async Task Re_importing_the_same_order_is_caught_and_creates_no_duplicate()
    {
        var repo = new InMemoryImportedOrderRepository();
        var uow = new FakeUnitOfWork();
        var json = """{"orderId":"AC0000122700"}""";

        await Handler(repo, uow).HandleAsync(new ImportOrder(json), TestContext.Current.CancellationToken);
        var second = await Handler(repo, uow).HandleAsync(new ImportOrder(json), TestContext.Current.CancellationToken);

        second.AlreadyImported.ShouldBeTrue();
        repo.All.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Unrecognized_payload_throws()
    {
        var handler = Handler(new InMemoryImportedOrderRepository(), new FakeUnitOfWork());

        await Should.ThrowAsync<UnrecognizedImportException>(() =>
            handler.HandleAsync(new ImportOrder("""{"somethingElse":1}"""), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    public async Task Invalid_json_throws_unrecognized(string raw)
    {
        var handler = Handler(new InMemoryImportedOrderRepository(), new FakeUnitOfWork());

        await Should.ThrowAsync<UnrecognizedImportException>(() =>
            handler.HandleAsync(new ImportOrder(raw), TestContext.Current.CancellationToken));
    }
}
