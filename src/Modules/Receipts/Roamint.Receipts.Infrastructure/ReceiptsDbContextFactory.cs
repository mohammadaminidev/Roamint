using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Roamint.Receipts.Infrastructure;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> only. The connection string is a placeholder used solely to build
/// the model during migration scaffolding; it is never opened at runtime (the host uses the Aspire-provided
/// connection via <see cref="ReceiptsPersistence.AddReceiptsPersistence"/>).
/// </summary>
internal sealed class ReceiptsDbContextFactory : IDesignTimeDbContextFactory<ReceiptsDbContext>
{
    public ReceiptsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReceiptsDbContext>()
            .UseNpgsql("Host=localhost;Database=roamint_design;Username=postgres;Password=postgres")
            .Options;

        return new ReceiptsDbContext(options, TimeProvider.System);
    }
}
