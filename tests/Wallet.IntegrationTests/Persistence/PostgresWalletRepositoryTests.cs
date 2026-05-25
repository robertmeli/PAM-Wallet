using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Infrastructure.Persistence.Postgres;
using Wallet.IntegrationTests.Fixtures;
using Xunit;

namespace Wallet.IntegrationTests.Persistence;

[Collection("Postgres")]
public sealed class PostgresWalletRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Save_NewWallet_CanBeRetrieved()
    {
        var dbFactory = fixture.CreateDbContextFactory();
        var repo = new PostgresWalletRepository(dbFactory, NullLogger<PostgresWalletRepository>.Instance);
        var playerId = $"pg-player-{Guid.NewGuid():N}";

        var wallet = WalletAggregate.Create(playerId);
        wallet.AddFunds(100m);

        await repo.Save(wallet);
        var loaded = await repo.Get(playerId);

        loaded.Should().NotBeNull();
        loaded!.Balance.Should().Be(100m);
        loaded.Version.Should().Be(1);
    }

    [Fact]
    public async Task Save_UpdateExistingWallet_PersistsNewBalance()
    {
        var dbFactory = fixture.CreateDbContextFactory();
        var repo = new PostgresWalletRepository(dbFactory, NullLogger<PostgresWalletRepository>.Instance);
        var playerId = $"pg-player-{Guid.NewGuid():N}";

        var wallet = WalletAggregate.Create(playerId);
        wallet.AddFunds(100m);
        await repo.Save(wallet);

        wallet.AddFunds(50m);
        await repo.Save(wallet);

        var loaded = await repo.Get(playerId);
        loaded!.Balance.Should().Be(150m);
        loaded.Version.Should().Be(2);
    }

    [Fact]
    public async Task Get_NonExistentPlayer_ReturnsNull()
    {
        var dbFactory = fixture.CreateDbContextFactory();
        var repo = new PostgresWalletRepository(dbFactory, NullLogger<PostgresWalletRepository>.Instance);

        var result = await repo.Get("pg-nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Save_ConcurrentUpdates_ThrowsConcurrencyException()
    {
        var dbFactory1 = fixture.CreateDbContextFactory();
        var dbFactory2 = fixture.CreateDbContextFactory();
        var playerId = $"pg-player-{Guid.NewGuid():N}";

        var repo1 = new PostgresWalletRepository(dbFactory1, NullLogger<PostgresWalletRepository>.Instance);
        var repo2 = new PostgresWalletRepository(dbFactory2, NullLogger<PostgresWalletRepository>.Instance);

        var wallet = WalletAggregate.Create(playerId);
        wallet.AddFunds(100m);
        await repo1.Save(wallet);

        var a = await repo1.Get(playerId);
        var b = await repo2.Get(playerId);

        a!.AddFunds(10m);
        b!.AddFunds(20m);

        await repo1.Save(a);

        var act = async () => await repo2.Save(b);
        await act.Should().ThrowAsync<Exception>("optimistic concurrency should reject the stale write");
    }
}
