using Wallet.Application.Ports;
using Wallet.Domain.Events;

namespace Wallet.ComponentTests.Fakes;

/// <summary>
/// Thread-safe in-memory repository that serialises access per playerId,
/// simulating the single-writer guarantee provided by Orleans grains or DB row locks.
/// </summary>
public sealed class ThreadSafeInMemoryWalletRepository : IWalletRepository
{
    private readonly Dictionary<string, WalletAggregate> _store = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<WalletAggregate?> Get(string playerId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _store.TryGetValue(playerId, out var w) ? w : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task Save(WalletAggregate wallet, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _store[wallet.PlayerId] = wallet;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveWithOutbox(WalletAggregate wallet, WalletEvent walletEvent, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _store[wallet.PlayerId] = wallet;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task AppendOutboxEvent(WalletEvent walletEvent, CancellationToken ct = default) => Task.CompletedTask;
}
