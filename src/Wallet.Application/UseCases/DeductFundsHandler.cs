using Wallet.Application.DTOs;
using Wallet.Application.Ports;
using Wallet.Domain.Events;

namespace Wallet.Application.UseCases;

public sealed class DeductFundsHandler(IWalletRepository repository)
{
    public async Task<WalletOperationResult> Handle(string playerId, decimal amount, CancellationToken ct = default)
    {
        var wallet = await repository.Get(playerId, ct);
        if (wallet is null)
            return new WalletOperationResult(false, amount, null, null, "WALLET_NOT_FOUND");

        var balanceBefore = wallet.Balance;
        var result = wallet.DeductFunds(amount);
        if (!result.IsSuccess)
        {
            await repository.AppendOutboxEvent(new WalletEvent(
                Guid.NewGuid().ToString(),
                playerId,
                WalletEventType.FundsDeductionRejected,
                amount,
                wallet.Balance,
                DateTime.UtcNow,
                wallet.Version), ct);

            return new WalletOperationResult(false, amount, balanceBefore, balanceBefore, result.Error);
        }

        await repository.SaveWithOutbox(wallet, result.Value!, ct);

        return new WalletOperationResult(true, amount, balanceBefore, wallet.Balance, null);
    }
}
