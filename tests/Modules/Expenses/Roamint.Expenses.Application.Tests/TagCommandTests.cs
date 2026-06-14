using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application;
using Roamint.Expenses.Application.Expenses;
using Roamint.Expenses.Application.Tags;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;
using Shouldly;

namespace Roamint.Expenses.Application.Tests;

public sealed class TagCommandTests
{
    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero));

    private static readonly DateOnly Today = new(2026, 5, 30);

    private static ExpenseLineInput Line(string description, CategoryId category, decimal amount,
        IReadOnlyList<TagId>? tagIds = null) =>
        new(description, category, amount, TagIds: tagIds);

    // ---- Tag CRUD ----

    [Fact]
    public async Task CreateTag_scoped_persists_and_saves()
    {
        var (tags, uow) = NewTagStore();
        var groceries = CategoryId.New(Clock);
        var handler = new CreateTagHandler(tags, uow, Clock);

        var id = await handler.HandleAsync(new CreateTag("Fruit", groceries), TestContext.Current.CancellationToken);

        id.Value.ShouldNotBe(Guid.Empty);
        var stored = tags.All.ShouldHaveSingleItem();
        stored.Name.ShouldBe("Fruit");
        stored.Scope.ShouldBe(groceries);
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task CreateTag_is_idempotent_on_active_name_and_scope()
    {
        var (tags, uow) = NewTagStore();
        var groceries = CategoryId.New(Clock);
        var existing = tags.Seed(Tag.Create(TagId.New(Clock), "Fruit", groceries));
        var handler = new CreateTagHandler(tags, uow, Clock);

        var id = await handler.HandleAsync(new CreateTag("  fruit  ", groceries), TestContext.Current.CancellationToken);

        id.ShouldBe(existing.Id);
        tags.All.Count.ShouldBe(1);
        uow.SaveCount.ShouldBe(0); // collision returns existing, no write
    }

    [Fact]
    public async Task CreateTag_same_name_different_scope_is_distinct()
    {
        var (tags, uow) = NewTagStore();
        var groceries = CategoryId.New(Clock);
        tags.Seed(Tag.Create(TagId.New(Clock), "Fruit", groceries));
        var handler = new CreateTagHandler(tags, uow, Clock);

        var globalId = await handler.HandleAsync(new CreateTag("Fruit", null), TestContext.Current.CancellationToken);

        tags.All.Count.ShouldBe(2);
        tags.All.ShouldContain(tag => tag.Id == globalId && tag.IsGlobal);
    }

    [Fact]
    public async Task RenameTag_onto_existing_active_name_throws()
    {
        var (tags, uow) = NewTagStore();
        var groceries = CategoryId.New(Clock);
        tags.Seed(Tag.Create(TagId.New(Clock), "Fruit", groceries));
        var veg = tags.Seed(Tag.Create(TagId.New(Clock), "Vegetables", groceries));
        var handler = new RenameTagHandler(tags, uow);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RenameTag(veg.Id, "Fruit"), TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task ArchiveTag_missing_throws_not_found()
    {
        var (tags, uow) = NewTagStore();
        var handler = new ArchiveTagHandler(tags, uow);

        await Should.ThrowAsync<TagNotFoundException>(() => handler.HandleAsync(
            new ArchiveTag(TagId.New(Clock)), TestContext.Current.CancellationToken));
    }

    // ---- Scope validation on Record ----

    [Fact]
    public async Task RecordExpense_global_tag_is_accepted_on_any_line()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var gift = tags.Seed(Tag.Create(TagId.New(Clock), "Gift"));
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        var id = await handler.HandleAsync(
            new RecordExpense("USD", Today, [Line("Toy", food.Id, 10m, [gift.Id])]),
            TestContext.Current.CancellationToken);

        expenses.All.ShouldHaveSingleItem().Lines.ShouldHaveSingleItem().TagIds.ShouldContain(gift.Id);
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task RecordExpense_scoped_tag_on_matching_category_is_accepted()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var groceries = categories.Seed(Category.Create(CategoryId.New(Clock), "Groceries"));
        var fruit = tags.Seed(Tag.Create(TagId.New(Clock), "Fruit", groceries.Id));
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await handler.HandleAsync(
            new RecordExpense("USD", Today, [Line("Apple", groceries.Id, 2m, [fruit.Id])]),
            TestContext.Current.CancellationToken);

        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task RecordExpense_scoped_tag_on_mismatched_category_throws()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var groceries = categories.Seed(Category.Create(CategoryId.New(Clock), "Groceries"));
        var transport = categories.Seed(Category.Create(CategoryId.New(Clock), "Transport"));
        var fruit = tags.Seed(Tag.Create(TagId.New(Clock), "Fruit", groceries.Id));
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense("USD", Today, [Line("Taxi", transport.Id, 5m, [fruit.Id])]),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecordExpense_archived_tag_throws()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var archived = Tag.Create(TagId.New(Clock), "Old");
        archived.Archive();
        tags.Seed(archived);
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(
            new RecordExpense("USD", Today, [Line("Item", food.Id, 5m, [archived.Id])]),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecordExpense_unknown_tag_throws_not_found()
    {
        var (expenses, categories, tags, uow) = NewStore();
        var food = categories.Seed(Category.Create(CategoryId.New(Clock), "Food"));
        var handler = new RecordExpenseHandler(expenses, categories, tags, uow, Clock);

        await Should.ThrowAsync<TagNotFoundException>(() => handler.HandleAsync(
            new RecordExpense("USD", Today, [Line("Item", food.Id, 5m, [TagId.New(Clock)])]),
            TestContext.Current.CancellationToken));
        uow.SaveCount.ShouldBe(0);
    }

    private static (InMemoryTagRepository, FakeUnitOfWork) NewTagStore() =>
        (new InMemoryTagRepository(), new FakeUnitOfWork());

    private static (InMemoryExpenseRepository, InMemoryCategoryRepository, InMemoryTagRepository, FakeUnitOfWork) NewStore() =>
        (new InMemoryExpenseRepository(), new InMemoryCategoryRepository(), new InMemoryTagRepository(), new FakeUnitOfWork());
}
