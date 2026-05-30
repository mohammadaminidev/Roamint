using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Roamint.Application.Abstractions;
using Roamint.Expenses.Application;
using Roamint.Expenses.Infrastructure;

namespace Roamint.Expenses.Contracts;

/// <summary>Composition root for the Expenses module — the only Expenses symbol the web host wires.</summary>
public static class ExpensesModule
{
    public static IHostApplicationBuilder AddExpensesModule(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddExpensesPersistence();
        builder.Services.AddApplicationDispatcher(typeof(CreateCategory).Assembly);
        builder.Services.AddScoped<IExpensesApi, ExpensesApi>();

        return builder;
    }
}
