using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Roamint.Expenses.Application;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// Persistence wiring for the Expenses module. Public so the module's <c>Contracts</c> project can compose
/// it from <c>AddExpensesModule</c>; keeps EF Core / Npgsql / Aspire references out of Contracts.
/// </summary>
public static class ExpensesPersistence
{
    public static IHostApplicationBuilder AddExpensesPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.AddNpgsqlDbContext<ExpensesDbContext>("roamintdb");

        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        return builder;
    }
}
