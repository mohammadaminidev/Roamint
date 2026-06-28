using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.ItemMappings;
using Roamint.Expenses.Domain;

namespace Roamint.Expenses.Application.Tests;

public sealed class RecordItemMappingTests
{
    private static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero));

    private static (RecordItemMappingHandler Handler, InMemoryItemMappingRepository Mappings, FakeItemEmbedder Embedder, FakeUnitOfWork Uow, CategoryId Cat)
        NewHandler()
    {
        var clock = NewClock();
        var categories = new InMemoryCategoryRepository();
        var cat = categories.Seed(Category.Create(CategoryId.New(clock), "Food")).Id;
        var mappings = new InMemoryItemMappingRepository();
        var embedder = new FakeItemEmbedder();
        var uow = new FakeUnitOfWork();
        return (new RecordItemMappingHandler(mappings, categories, embedder, uow, clock), mappings, embedder, uow, cat);
    }

    [Fact]
    public async Task Creates_new_mapping_with_embedding_of_normalized_text_and_saves()
    {
        var (handler, mappings, embedder, uow, cat) = NewHandler();

        await handler.HandleAsync(
            new RecordItemMapping("  Պանիր Չանախ ", "پنیر چاناخ", "g", cat), TestContext.Current.CancellationToken);

        // Normalisation trims, collapses whitespace, and lowercases (the exact match key, 0021).
        var mapping = mappings.All.ShouldHaveSingleItem();
        mapping.SourceText.ShouldBe("պանիր չանախ");
        mapping.CanonicalDescription.ShouldBe("پنیر چاناخ");
        mapping.Unit.ShouldBe("g");
        mapping.CategoryId.ShouldBe(cat);
        mappings.Embeddings.ShouldContainKey(mapping.Id);
        embedder.Embedded.ShouldHaveSingleItem().ShouldBe("պանիր չանախ");
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Reconfirming_same_source_updates_in_place()
    {
        var (handler, mappings, _, uow, cat) = NewHandler();
        var ct = TestContext.Current.CancellationToken;

        await handler.HandleAsync(new RecordItemMapping("Պանիր", "پنیر", "g", cat), ct);
        await handler.HandleAsync(new RecordItemMapping("Պանիր", "پنیر سفید", "Pcs", cat), ct);

        var mapping = mappings.All.ShouldHaveSingleItem();
        mapping.CanonicalDescription.ShouldBe("پنیر سفید");
        mapping.Unit.ShouldBe("Pcs");
        uow.SaveCount.ShouldBe(2);
    }

    [Fact]
    public async Task Rejects_unknown_category_without_saving()
    {
        var (handler, mappings, _, uow, _) = NewHandler();

        await Should.ThrowAsync<CategoryNotFoundException>(() =>
            handler.HandleAsync(
                new RecordItemMapping("Պանիր", "پنیر", null, CategoryId.New(NewClock())),
                TestContext.Current.CancellationToken));

        mappings.All.ShouldBeEmpty();
        uow.SaveCount.ShouldBe(0);
    }
}
