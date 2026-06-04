using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain.Tests;

public class TagTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-05-29T12:00:00Z", CultureInfo.InvariantCulture));
    private static readonly CategoryId Groceries = CategoryId.New(Clock);

    [Fact]
    public void Create_global_trims_name_and_raises_event()
    {
        var id = TagId.New(Clock);
        var tag = Tag.Create(id, "  For Happiness  ");

        tag.Name.ShouldBe("For Happiness");
        tag.Status.ShouldBe(TagStatus.Active);
        tag.Scope.ShouldBeNull();
        tag.IsGlobal.ShouldBeTrue();
        var ev = tag.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TagCreated>();
        ev.Name.ShouldBe("For Happiness");
        ev.Scope.ShouldBeNull();
    }

    [Fact]
    public void Create_scoped_carries_scope_in_state_and_event()
    {
        var tag = Tag.Create(TagId.New(Clock), "Fruit", Groceries);

        tag.IsGlobal.ShouldBeFalse();
        tag.Scope.ShouldBe(Groceries);
        tag.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TagCreated>().Scope.ShouldBe(Groceries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_name(string name)
    {
        Should.Throw<ArgumentException>(() => Tag.Create(TagId.New(Clock), name));
    }

    [Fact]
    public void Create_rejects_overly_long_name()
    {
        var tooLong = new string('x', Tag.MaxNameLength + 1);
        Should.Throw<DomainException>(() => Tag.Create(TagId.New(Clock), tooLong));
    }

    [Fact]
    public void Rename_to_same_name_is_noop()
    {
        var tag = Tag.Create(TagId.New(Clock), "Fruit", Groceries);
        tag.ClearDomainEvents();

        tag.Rename("Fruit");

        tag.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Rename_changes_name_and_raises_event()
    {
        var tag = Tag.Create(TagId.New(Clock), "Fruit", Groceries);
        tag.ClearDomainEvents();

        tag.Rename("Produce");

        tag.Name.ShouldBe("Produce");
        var ev = tag.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TagRenamed>();
        ev.PreviousName.ShouldBe("Fruit");
        ev.NewName.ShouldBe("Produce");
    }

    [Fact]
    public void Archive_is_idempotent()
    {
        var tag = Tag.Create(TagId.New(Clock), "Fruit", Groceries);
        tag.ClearDomainEvents();

        tag.Archive();
        tag.Archive();

        tag.Status.ShouldBe(TagStatus.Archived);
        tag.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TagArchived>();
    }

    [Fact]
    public void Global_tag_applies_to_any_category()
    {
        var tag = Tag.Create(TagId.New(Clock), "Gift");

        tag.AppliesTo(Groceries).ShouldBeTrue();
        tag.AppliesTo(CategoryId.New(Clock)).ShouldBeTrue();
    }

    [Fact]
    public void Scoped_tag_applies_only_to_its_category()
    {
        var tag = Tag.Create(TagId.New(Clock), "Fruit", Groceries);

        tag.AppliesTo(Groceries).ShouldBeTrue();
        tag.AppliesTo(CategoryId.New(Clock)).ShouldBeFalse();
    }
}
