using Orleans;
using Wallet.Domain.Enums;

namespace Wallet.Infrastructure.Orchestration.Orleans;

public interface IWalletGrain : IGrainWithStringKey
{
    Task<WalletGrainBalanceResult?> CreateWallet(WalletType walletType = WalletType.Main,
        CurrencyType currencyType = CurrencyType.EUR, DateTime? expiresAt = null);
    Task<WalletGrainOperationResult> AddFunds(decimal amount, WalletType walletType = WalletType.Main,
        CurrencyType currencyType = CurrencyType.EUR, DateTime? expiresAt = null);
    Task<WalletGrainOperationResult> DeductFunds(decimal amount);
    Task<WalletGrainBalanceResult?> GetBalance();
}
