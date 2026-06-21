using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain.Tests;

public class ItemMappingTests
{
    private static readonly FakeTimeProvider Clock =
        new(DateTimeOffset.Parse("2026-06-20T10:00:00Z", CultureInfo.InvariantCulture));

    private static readonly CategoryId Cat = CategoryId.New(Clock);

    [Fact]
    public void Create_normalizes_source_and_sets_fields()
    {
        var mapping = ItemMapping.Create(
            ItemMappingId.New(Clock), "  Պանիր   Չանախ  ", "پنیر چاناخ", "g", Cat, Clock);

        mapping.SourceText.ShouldBe("Պանիր Չանախ");
        mapping.CanonicalDescription.ShouldBe("پنیر چاناخ");
        mapping.Unit.ShouldBe("g");
        mapping.CategoryId.ShouldBe(Cat);
        mapping.LastUsedOnUtc.ShouldBe(Clock.GetUtcNow());
    }

    [Fact]
    public void Create_allows_null_unit()
    {
        var mapping = ItemMapping.Create(ItemMappingId.New(Clock), "Լայմ", "لیمو", null, Cat, Clock);

        mapping.Unit.ShouldBeNull();
    }

    [Fact]
    public void Create_rejects_blank_source() =>
        Should.Throw<ArgumentException>(() =>
            ItemMapping.Create(ItemMappingId.New(Clock), "   ", "پنیر", null, Cat, Clock));

    [Fact]
    public void Create_rejects_blank_description() =>
        Should.Throw<ArgumentException>(() =>
            ItemMapping.Create(ItemMappingId.New(Clock), "Պանիր", "  ", null, Cat, Clock));

    [Fact]
    public void Create_rejects_unknown_unit() =>
        Should.Throw<DomainException>(() =>
            ItemMapping.Create(ItemMappingId.New(Clock), "Պանիր", "پنیر", "kg", Cat, Clock));

    [Fact]
    public void Revise_repoints_resolution_keeping_source_and_stamps_time()
    {
        var clock = new FakeTimeProvider(Clock.GetUtcNow());
        var mapping = ItemMapping.Create(ItemMappingId.New(clock), "Պանիր", "پنیر", "g", Cat, clock);
        var newCat = CategoryId.New(clock);
        clock.Advance(TimeSpan.FromMinutes(5));

        mapping.Revise("پنیر سفید", "Pcs", newCat, clock);

        mapping.CanonicalDescription.ShouldBe("پنیر سفید");
        mapping.Unit.ShouldBe("Pcs");
        mapping.CategoryId.ShouldBe(newCat);
        mapping.LastUsedOnUtc.ShouldBe(clock.GetUtcNow());
        mapping.SourceText.ShouldBe("Պանիր");
    }
}
