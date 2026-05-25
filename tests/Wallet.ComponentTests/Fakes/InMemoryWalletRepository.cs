using Wallet.Application.Ports;
using Wallet.Domain.Events;

namespace Wallet.ComponentTests.Fakes;

public sealed class InMemoryWalletRepository : IWalletRepository
{
    private readonly Dictionary<string, WalletAggregate> _store = new();

    public Task<WalletAggregate?> Get(string playerId, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(playerId, out var w) ? w : null);

    public Task Save(WalletAggregate wallet, CancellationToken ct = default)
    {
        _store[wallet.PlayerId] = wallet;
        return Task.CompletedTask;
    }

    public Task SaveWithOutbox(WalletAggregate wallet, WalletEvent walletEvent, CancellationToken ct = default)
    {
        _store[wallet.PlayerId] = wallet;
        return Task.CompletedTask;
    }

    public Task AppendOutboxEvent(WalletEvent walletEvent, CancellationToken ct = default) => Task.CompletedTask;
}
