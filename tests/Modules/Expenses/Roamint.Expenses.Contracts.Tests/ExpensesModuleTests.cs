using Microsoft.Extensions.DependencyInjection;
using Roamint.Expenses.Application;
using Roamint.Expenses.Domain;
using Roamint.SharedKernel;

namespace Roamint.Expenses.Contracts.Tests;

[Collection(nameof(ModuleHostCollection))]
public sealed class ExpensesModuleTests(ModuleHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Records_a_multi_category_expense_end_to_end_through_the_module_api()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();

        var food = await api.CreateCategoryAsync(new CreateCategoryRequest("Food"), Ct);
        var hygiene = await api.CreateCategoryAsync(new CreateCategoryRequest("Hygiene"), Ct);
        var expenseId = await api.RecordExpenseAsync(
            new RecordExpenseRequest(
                "AMD",
                new DateOnly(2026, 4, 15),
                [
                    new ExpenseLineDto("Apple", food, 400m, QuantityValue: 500m, QuantityUnit: "g"),
                    new ExpenseLineDto("Shampoo", hygiene, 1000m),
                ],
                Note: "shop"),
            Ct);

        expenseId.ShouldNotBe(Guid.Empty);

        // Read back through the public repository port (no read API yet) to prove it actually persisted.
        using var readScope = fixture.Services.CreateScope();
        var expenses = readScope.ServiceProvider.GetRequiredService<IExpenseRepository>();
        var expense = await expenses.GetAsync(new ExpenseId(expenseId), Ct);

        expense.ShouldNotBeNull();
        expense.Total.ShouldBe(Money.Of(1400m, Currency.Amd));

        var apple = expense.Lines.Single(line => line.Description == "Apple");
        apple.CategoryId.ShouldBe(new CategoryId(food));
        apple.Amount.ShouldBe(Money.Of(400m, Currency.Amd));
        apple.Quantity.ShouldBe(Quantity.Of(500m, QuantityUnit.Grams));

        expense.Lines.Single(line => line.Description == "Shampoo").CategoryId.ShouldBe(new CategoryId(hygiene));
    }

    [Fact]
    public async Task Recording_against_an_archived_category_surfaces_a_DomainException()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();

        var categoryId = await api.CreateCategoryAsync(new CreateCategoryRequest("Retired"), Ct);
        await api.ArchiveCategoryAsync(categoryId, Ct);

        await Should.ThrowAsync<DomainException>(() =>
            api.RecordExpenseAsync(
                new RecordExpenseRequest("USD", new DateOnly(2026, 4, 1), [new ExpenseLineDto("Item", categoryId, 5m)]), Ct));
    }

    [Fact]
    public async Task Unknown_currency_code_surfaces_a_DomainException()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();

        var categoryId = await api.CreateCategoryAsync(new CreateCategoryRequest("Misc"), Ct);

        await Should.ThrowAsync<DomainException>(() =>
            api.RecordExpenseAsync(
                new RecordExpenseRequest("XYZ", new DateOnly(2026, 4, 1), [new ExpenseLineDto("Item", categoryId, 5m)]), Ct));
    }

    [Fact]
    public async Task Unknown_quantity_unit_code_surfaces_a_DomainException()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();

        var categoryId = await api.CreateCategoryAsync(new CreateCategoryRequest("Grocery"), Ct);

        await Should.ThrowAsync<DomainException>(() =>
            api.RecordExpenseAsync(
                new RecordExpenseRequest("USD", new DateOnly(2026, 4, 1),
                    [new ExpenseLineDto("Apple", categoryId, 5m, QuantityValue: 1m, QuantityUnit: "kg")]), Ct));
    }
}
