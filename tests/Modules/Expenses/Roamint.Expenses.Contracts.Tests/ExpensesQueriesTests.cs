using Microsoft.Extensions.DependencyInjection;
using Roamint.Expenses.Contracts;

namespace Roamint.Expenses.Contracts.Tests;

[Collection(nameof(ModuleHostCollection))]
public sealed class ExpensesQueriesTests(ModuleHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_a_created_category_and_drops_it_once_archived()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();
        var queries = scope.ServiceProvider.GetRequiredService<IExpensesQueries>();

        var name = $"Queryable-{Guid.NewGuid():N}";
        var categoryId = await api.CreateCategoryAsync(new CreateCategoryRequest(name), Ct);

        var afterCreate = await queries.ListActiveCategoriesAsync(Ct);
        afterCreate.ShouldContain(category => category.Id == categoryId && category.Name == name);

        await api.ArchiveCategoryAsync(categoryId, Ct);

        var afterArchive = await queries.ListActiveCategoriesAsync(Ct);
        afterArchive.ShouldNotContain(category => category.Id == categoryId);
    }

    [Fact]
    public async Task Creates_tags_filters_picker_by_category_and_records_line_tags_through_the_wiring()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();
        var queries = scope.ServiceProvider.GetRequiredService<IExpensesQueries>();

        var token = $"Tag-{Guid.NewGuid():N}";
        var groceries = await api.CreateCategoryAsync(new CreateCategoryRequest($"{token} Groceries"), Ct);
        var transport = await api.CreateCategoryAsync(new CreateCategoryRequest($"{token} Transport"), Ct);

        var fruit = await api.CreateTagAsync(new CreateTagRequest($"{token} Fruit", groceries), Ct);
        var gift = await api.CreateTagAsync(new CreateTagRequest($"{token} Gift", null), Ct);

        // Idempotent create: same active (name, scope) returns the existing id (D5 inline-create path).
        var fruitAgain = await api.CreateTagAsync(new CreateTagRequest($"{token} fruit", groceries), Ct);
        fruitAgain.ShouldBe(fruit);

        // Picker for a Groceries line: global ∪ scoped-to-Groceries; the Transport-scoped tag is hidden.
        var commute = await api.CreateTagAsync(new CreateTagRequest($"{token} Commute", transport), Ct);
        var picker = await queries.ListTagsAsync(groceries, Ct);
        var mine = picker.Where(tag => tag.Name.StartsWith(token, StringComparison.Ordinal)).Select(tag => tag.Id).ToList();
        mine.ShouldContain(fruit);
        mine.ShouldContain(gift);
        mine.ShouldNotContain(commute);

        // Record a line carrying both tags; the suggestion remembers them.
        await api.RecordExpenseAsync(
            new RecordExpenseRequest("USD", new DateOnly(2026, 4, 10),
                [new ExpenseLineDto($"{token} Apple", groceries, 2.50m, TagIds: [fruit, gift])]), Ct);

        var suggestions = await queries.SuggestItemsAsync($"{token} Apple", 5, Ct);
        var apple = suggestions.ShouldHaveSingleItem();
        apple.TagIds.ShouldBe([fruit, gift], ignoreOrder: true);
    }

    [Fact]
    public async Task Suggests_a_recorded_item_and_its_category_through_the_wiring()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();
        var queries = scope.ServiceProvider.GetRequiredService<IExpensesQueries>();

        var token = $"Suggestable-{Guid.NewGuid():N}";
        var categoryId = await api.CreateCategoryAsync(new CreateCategoryRequest($"{token} Cat"), Ct);
        await api.RecordExpenseAsync(
            new RecordExpenseRequest("USD", new DateOnly(2026, 4, 10),
                [new ExpenseLineDto(token, categoryId, 4.50m, QuantityValue: 250m, QuantityUnit: "ml")]), Ct);

        var suggestions = await queries.SuggestItemsAsync(token, 5, Ct);
        suggestions.ShouldContain(s => s.Text == token && s.CategoryId == categoryId && s.Unit == "ml");

        var suggested = await queries.SuggestCategoryAsync(token, Ct);
        suggested.ShouldBe(categoryId);

        var none = await queries.SuggestCategoryAsync($"absent-{Guid.NewGuid():N}", Ct);
        none.ShouldBeNull();
    }
}
