using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wallet.Infrastructure.Persistence.Postgres;

public sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=wallet;Username=postgres;Password=postgres")
            .Options;

        return new WalletDbContext(options);
    }
}
