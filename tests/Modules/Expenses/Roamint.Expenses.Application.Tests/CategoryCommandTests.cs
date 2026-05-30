using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;
using Shouldly;

namespace Roamint.Expenses.Application.Tests;

public sealed class CategoryCommandTests
{
    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreateCategory_persists_returns_id_and_saves_once()
    {
        var categories = new InMemoryCategoryRepository();
        var uow = new FakeUnitOfWork();
        var handler = new CreateCategoryHandler(categories, uow, Clock);

        var id = await handler.HandleAsync(new CreateCategory("Groceries"), TestContext.Current.CancellationToken);

        id.Value.ShouldNotBe(Guid.Empty);
        var stored = categories.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(id);
        stored.Name.ShouldBe("Groceries");
        stored.Status.ShouldBe(CategoryStatus.Active);
        stored.DomainEvents.OfType<CategoryCreated>().ShouldHaveSingleItem();
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task RenameCategory_missing_throws_not_found()
    {
        var categories = new InMemoryCategoryRepository();
        var uow = new FakeUnitOfWork();
        var handler = new RenameCategoryHandler(categories, uow);

        await Should.ThrowAsync<CategoryNotFoundException>(() =>
            handler.HandleAsync(new RenameCategory(CategoryId.New(Clock), "X"), TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RenameCategory_updates_name_and_saves()
    {
        var categories = new InMemoryCategoryRepository();
        var seeded = categories.Seed(Category.Create(CategoryId.New(Clock), "Old"));
        var uow = new FakeUnitOfWork();
        var handler = new RenameCategoryHandler(categories, uow);

        await handler.HandleAsync(new RenameCategory(seeded.Id, "New"), TestContext.Current.CancellationToken);

        seeded.Name.ShouldBe("New");
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task ArchiveCategory_missing_throws_not_found()
    {
        var categories = new InMemoryCategoryRepository();
        var handler = new ArchiveCategoryHandler(categories, new FakeUnitOfWork());

        await Should.ThrowAsync<CategoryNotFoundException>(() =>
            handler.HandleAsync(new ArchiveCategory(CategoryId.New(Clock)), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ArchiveCategory_sets_archived_and_saves()
    {
        var categories = new InMemoryCategoryRepository();
        var seeded = categories.Seed(Category.Create(CategoryId.New(Clock), "Travel"));
        var uow = new FakeUnitOfWork();
        var handler = new ArchiveCategoryHandler(categories, uow);

        await handler.HandleAsync(new ArchiveCategory(seeded.Id), TestContext.Current.CancellationToken);

        seeded.Status.ShouldBe(CategoryStatus.Archived);
        uow.SaveCount.ShouldBe(1);
    }
}
