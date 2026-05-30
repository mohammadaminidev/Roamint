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
    public async Task Records_an_expense_end_to_end_through_the_module_api()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();

        var categoryId = await api.CreateCategoryAsync(new CreateCategoryRequest("Food"), Ct);
        var expenseId = await api.RecordExpenseAsync(
            new RecordExpenseRequest(
                categoryId,
                12.50m,
                "USD",
                new DateOnly(2026, 4, 15),
                Note: "lunch",
                Lines: [new ExpenseLineDto("Sandwich", 1m, 12.50m)]),
            Ct);

        categoryId.ShouldNotBe(Guid.Empty);
        expenseId.ShouldNotBe(Guid.Empty);

        // Read back through the public repository port (no read API yet) to prove it actually persisted.
        using var readScope = fixture.Services.CreateScope();
        var expenses = readScope.ServiceProvider.GetRequiredService<IExpenseRepository>();
        var expense = await expenses.GetAsync(new ExpenseId(expenseId), Ct);

        expense.ShouldNotBeNull();
        expense.Amount.ShouldBe(Money.Of(12.50m, Currency.Usd));
        expense.CategoryId.ShouldBe(new CategoryId(categoryId));
        expense.Lines.ShouldHaveSingleItem().Description.ShouldBe("Sandwich");
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
                new RecordExpenseRequest(categoryId, 5m, "USD", new DateOnly(2026, 4, 1)), Ct));
    }

    [Fact]
    public async Task Unknown_currency_code_surfaces_a_DomainException()
    {
        using var scope = fixture.Services.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IExpensesApi>();

        var categoryId = await api.CreateCategoryAsync(new CreateCategoryRequest("Misc"), Ct);

        await Should.ThrowAsync<DomainException>(() =>
            api.RecordExpenseAsync(
                new RecordExpenseRequest(categoryId, 5m, "XYZ", new DateOnly(2026, 4, 1)), Ct));
    }
}
