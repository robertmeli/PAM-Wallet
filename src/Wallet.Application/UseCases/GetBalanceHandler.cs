using Wallet.Application.DTOs;
using Wallet.Application.Ports;

namespace Wallet.Application.UseCases;

public sealed class GetBalanceHandler(IWalletRepository repository)
{
    public async Task<BalanceResult?> Handle(string playerId, CancellationToken ct = default)
    {
        var wallet = await repository.Get(playerId, ct);
        if (wallet is null)
            return null;

        return new BalanceResult(wallet.PlayerId, wallet.Balance,
            wallet.WalletType, wallet.CurrencyType, wallet.CreatedAt, wallet.ExpiresAt);
    }
}
