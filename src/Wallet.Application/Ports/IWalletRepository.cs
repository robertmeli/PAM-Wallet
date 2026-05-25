using Wallet.Domain.Events;

namespace Wallet.Application.Ports;

public interface IWalletRepository
{
    Task<WalletAggregate?> Get(string playerId, CancellationToken ct = default);
    Task Save(WalletAggregate wallet, CancellationToken ct = default);
    Task SaveWithOutbox(WalletAggregate wallet, WalletEvent walletEvent, CancellationToken ct = default);
    Task AppendOutboxEvent(WalletEvent walletEvent, CancellationToken ct = default);
}
