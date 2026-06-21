using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace Roamint.Expenses.Infrastructure;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> only. The connection string is a placeholder used solely to
/// build the model during migration scaffolding; it is never opened at runtime (the host uses the
/// Aspire-provided connection via <see cref="ExpensesPersistence.AddExpensesPersistence"/>).
/// </summary>
internal sealed class ExpensesDbContextFactory : IDesignTimeDbContextFactory<ExpensesDbContext>
{
    public ExpensesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=roamint_design;Username=postgres;Password=postgres",
                npgsql => npgsql.UseVector())
            .Options;

        return new ExpensesDbContext(options, TimeProvider.System);
    }
}
