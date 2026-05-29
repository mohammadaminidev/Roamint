using Microsoft.Extensions.Time.Testing;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain.Tests;

public class CategoryTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-05-29T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Create_trims_name_and_raises_event()
    {
        var id = CategoryId.New(Clock);
        var category = Category.Create(id, "  Groceries  ");

        category.Name.ShouldBe("Groceries");
        category.Status.ShouldBe(CategoryStatus.Active);
        category.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CategoryCreated>()
            .Name.ShouldBe("Groceries");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_name(string name)
    {
        Should.Throw<ArgumentException>(() => Category.Create(CategoryId.New(Clock), name));
    }

    [Fact]
    public void Create_rejects_overly_long_name()
    {
        var tooLong = new string('x', Category.MaxNameLength + 1);
        Should.Throw<DomainException>(() => Category.Create(CategoryId.New(Clock), tooLong));
    }

    [Fact]
    public void Rename_to_same_name_is_noop()
    {
        var category = Category.Create(CategoryId.New(Clock), "Groceries");
        category.ClearDomainEvents();

        category.Rename("Groceries");

        category.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Rename_changes_name_and_raises_event()
    {
        var category = Category.Create(CategoryId.New(Clock), "Groceries");
        category.ClearDomainEvents();

        category.Rename("Food");

        category.Name.ShouldBe("Food");
        var ev = category.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CategoryRenamed>();
        ev.PreviousName.ShouldBe("Groceries");
        ev.NewName.ShouldBe("Food");
    }

    [Fact]
    public void Archive_is_idempotent()
    {
        var category = Category.Create(CategoryId.New(Clock), "Groceries");
        category.ClearDomainEvents();

        category.Archive();
        category.Archive();

        category.Status.ShouldBe(CategoryStatus.Archived);
        category.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CategoryArchived>();
    }
}
