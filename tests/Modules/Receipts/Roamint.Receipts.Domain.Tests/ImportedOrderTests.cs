using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;

namespace Roamint.Receipts.Domain.Tests;

public class ImportedOrderTests
{
    private static readonly FakeTimeProvider Clock =
        new(DateTimeOffset.Parse("2026-06-21T12:00:00Z", CultureInfo.InvariantCulture));

    private static ParsedDraft SampleDraft() => new(
        "Yerevan City",
        new DateOnly(2026, 6, 21),
        "AMD",
        [new ParsedLine("Eggs", 990m, 1m, "Pcs", "Eggs"), new ParsedLine("Cherry", 1287.04m, 722m, "g", "Cherry")]);

    private static ImportedOrder Imported() =>
        ImportedOrder.Import(ImportedOrderId.New(Clock), "YerevanCity", "AC0000122700", SampleDraft(), Clock);

    [Fact]
    public void Import_sets_imported_state_and_raises_event()
    {
        var id = ImportedOrderId.New(Clock);

        var order = ImportedOrder.Import(id, "YerevanCity", "AC0000122700", SampleDraft(), Clock);

        order.Status.ShouldBe(ImportedOrderStatus.Imported);
        order.Source.ShouldBe("YerevanCity");
        order.ExternalOrderId.ShouldBe("AC0000122700");
        order.LinkedExpenseId.ShouldBeNull();
        order.ImportedOnUtc.ShouldBe(Clock.GetUtcNow());

        var ev = order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderImported>();
        ev.OrderId.ShouldBe(id);
        ev.Source.ShouldBe("YerevanCity");
        ev.ExternalOrderId.ShouldBe("AC0000122700");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_rejects_blank_source(string source) =>
        Should.Throw<DomainException>(() =>
            ImportedOrder.Import(ImportedOrderId.New(Clock), source, "AC1", SampleDraft(), Clock));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_rejects_blank_external_order_id(string externalId) =>
        Should.Throw<DomainException>(() =>
            ImportedOrder.Import(ImportedOrderId.New(Clock), "YerevanCity", externalId, SampleDraft(), Clock));

    [Fact]
    public void Import_rejects_an_empty_draft() =>
        Should.Throw<DomainException>(() =>
            ImportedOrder.Import(ImportedOrderId.New(Clock), "YerevanCity", "AC1", ParsedDraft.Empty, Clock));

    [Fact]
    public void LinkTo_sets_expense_and_transitions_to_linked()
    {
        var order = Imported();
        var expenseId = Guid.CreateVersion7();

        order.LinkTo(expenseId);

        order.Status.ShouldBe(ImportedOrderStatus.Linked);
        order.LinkedExpenseId.ShouldBe(expenseId);
        order.DomainEvents.OfType<ImportedOrderLinked>().ShouldHaveSingleItem().ExpenseId.ShouldBe(expenseId);
    }

    [Fact]
    public void LinkTo_rejects_empty_expense_id() =>
        Should.Throw<DomainException>(() => Imported().LinkTo(Guid.Empty));

    [Fact]
    public void Linked_is_terminal()
    {
        var order = Imported();
        order.LinkTo(Guid.CreateVersion7());

        Should.Throw<DomainException>(() => order.LinkTo(Guid.CreateVersion7()));
    }
}
