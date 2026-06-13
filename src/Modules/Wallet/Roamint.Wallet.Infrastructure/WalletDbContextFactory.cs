using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Roamint.Wallet.Infrastructure;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> only. The connection string is a placeholder used solely to
/// build the model during migration scaffolding; it is never opened at runtime (the host uses the
/// Aspire-provided connection via <see cref="WalletPersistence.AddWalletPersistence"/>).
/// </summary>
internal sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=roamint_design;Username=postgres;Password=postgres")
            .Options;

        return new WalletDbContext(options, TimeProvider.System);
    }
}
