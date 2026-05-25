using Wallet.Application.DTOs;
using Wallet.Application.Ports;
using Wallet.Domain.Enums;

namespace Wallet.Application.UseCases;

public sealed class CreateWalletHandler(IWalletRepository repository)
{
    public async Task<BalanceResult?> Handle(string playerId, WalletType walletType,
        CurrencyType currencyType, DateTime? expiresAt, CancellationToken ct = default)
    {
        var existing = await repository.Get(playerId, ct);
        if (existing is not null)
            return null;

        var wallet = WalletAggregate.Create(playerId, walletType, currencyType, expiresAt);
        await repository.Save(wallet, ct);

        return new BalanceResult(wallet.PlayerId, wallet.Balance,
            wallet.WalletType, wallet.CurrencyType, wallet.CreatedAt, wallet.ExpiresAt);
    }
}
