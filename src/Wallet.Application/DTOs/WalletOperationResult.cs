using Wallet.Domain.Enums;

namespace Wallet.Application.DTOs;

public sealed record WalletOperationResult(
    bool Success,
    decimal? Amount,
    decimal? BalanceBefore,
    decimal? BalanceAfter,
    string? Error);

public sealed record BalanceResult(
    string PlayerId,
    decimal Balance,
    WalletType WalletType,
    CurrencyType CurrencyType,
    DateTime CreatedAt,
    DateTime? ExpiresAt);
