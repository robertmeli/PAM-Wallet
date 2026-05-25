using Wallet.Application.DTOs;
using Wallet.Application.Ports;
using Wallet.Domain.Common;
using Wallet.Domain.Enums;

namespace Wallet.Application.UseCases;

public sealed class AddFundsHandler(IWalletRepository repository)
{
    public async Task<WalletOperationResult> Handle(string playerId, decimal amount,
        WalletType walletType = WalletType.Main, CurrencyType currencyType = CurrencyType.EUR,
        DateTime? expiresAt = null, CancellationToken ct = default)
    {
        if (amount <= 0)
            return new WalletOperationResult(false, amount, null, null, DomainErrors.InvalidAmount);

        var wallet = await repository.Get(playerId, ct)
            ?? WalletAggregate.Create(playerId, walletType, currencyType, expiresAt);

        var balanceBefore = wallet.Balance;
        var result = wallet.AddFunds(amount);
        if (!result.IsSuccess)
            return new WalletOperationResult(false, amount, balanceBefore, wallet.Balance, result.Error);

        await repository.SaveWithOutbox(wallet, result.Value!, ct);
        return new WalletOperationResult(true, amount, balanceBefore, wallet.Balance, null);
    }
}
