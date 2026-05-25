using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Wallet.Infrastructure.Persistence.Postgres;
using Xunit;

namespace Wallet.IntegrationTests.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithDatabase("wallet_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public WalletDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new WalletDbContext(opts);
    }

    public IDbContextFactory<WalletDbContext> CreateDbContextFactory()
    {
        var opts = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new TestWalletDbContextFactory(opts);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() =>
        await _container.DisposeAsync();

    private sealed class TestWalletDbContextFactory(DbContextOptions<WalletDbContext> options)
        : IDbContextFactory<WalletDbContext>
    {
        public WalletDbContext CreateDbContext() => new(options);

        public Task<WalletDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
