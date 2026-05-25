using Wallet.Domain.Common;
using Wallet.Domain.Enums;
using Wallet.Domain.Events;

namespace Wallet.Domain.Aggregates;

public sealed class Wallet
{
    public string PlayerId { get; }
    public decimal Balance { get; private set; }
    public long Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public WalletType WalletType { get; }
    public CurrencyType CurrencyType { get; }
    public DateTime CreatedAt { get; }
    public DateTime? ExpiresAt { get; }

    private Wallet(string playerId, decimal balance, long version, DateTime updatedAtUtc,
        WalletType walletType, CurrencyType currencyType, DateTime createdAt, DateTime? expiresAt)
    {
        PlayerId = playerId;
        Balance = balance;
        Version = version;
        UpdatedAtUtc = updatedAtUtc;
        WalletType = walletType;
        CurrencyType = currencyType;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public static Wallet Create(string playerId, WalletType walletType = WalletType.Main,
        CurrencyType currencyType = CurrencyType.EUR, DateTime? expiresAt = null)
    {
        var now = DateTime.UtcNow;
        return new(playerId, 0m, 0, now, walletType, currencyType, now, expiresAt);
    }

    public static Wallet Reconstitute(string playerId, decimal balance, long version, DateTime updatedAtUtc,
        WalletType walletType, CurrencyType currencyType, DateTime createdAt, DateTime? expiresAt) =>
        new(playerId, balance, version, updatedAtUtc, walletType, currencyType, createdAt, expiresAt);

    public Result<WalletEvent> AddFunds(decimal amount)
    {
        if (amount <= 0)
            return Result<WalletEvent>.Failure(DomainErrors.InvalidAmount);

        Balance += amount;
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result<WalletEvent>.Success(new WalletEvent(
            Guid.NewGuid().ToString(),
            PlayerId,
            WalletEventType.FundsAdded,
            amount,
            Balance,
            UpdatedAtUtc,
            Version));
    }

    public Result<WalletEvent> DeductFunds(decimal amount)
    {
        if (amount <= 0)
            return Result<WalletEvent>.Failure(DomainErrors.InvalidAmount);

        if (Balance < amount)
            return Result<WalletEvent>.Failure(DomainErrors.InsufficientFunds);

        Balance -= amount;
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result<WalletEvent>.Success(new WalletEvent(
            Guid.NewGuid().ToString(),
            PlayerId,
            WalletEventType.FundsDeducted,
            amount,
            Balance,
            UpdatedAtUtc,
            Version));
    }
}
