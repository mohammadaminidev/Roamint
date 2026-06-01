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
}
