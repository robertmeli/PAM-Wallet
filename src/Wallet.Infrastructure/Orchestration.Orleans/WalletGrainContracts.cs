using Orleans;
using Wallet.Domain.Enums;

namespace Wallet.Infrastructure.Orchestration.Orleans;

[GenerateSerializer]
public sealed class WalletGrainState
{
    [Id(0)] public bool IsLoaded { get; set; }
    [Id(1)] public decimal Balance { get; set; }
    [Id(2)] public long Version { get; set; }
    [Id(3)] public DateTime UpdatedAtUtc { get; set; }
    [Id(4)] public WalletType WalletType { get; set; }
    [Id(5)] public CurrencyType CurrencyType { get; set; }
    [Id(6)] public DateTime CreatedAt { get; set; }
    [Id(7)] public DateTime? ExpiresAt { get; set; }
}

[GenerateSerializer]
public sealed record WalletGrainOperationResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] decimal? Amount,
    [property: Id(2)] decimal? BalanceBefore,
    [property: Id(3)] decimal? BalanceAfter,
    [property: Id(4)] string? Error);

[GenerateSerializer]
public sealed record WalletGrainBalanceResult(
    [property: Id(0)] string PlayerId,
    [property: Id(1)] decimal Balance,
    [property: Id(2)] WalletType WalletType,
    [property: Id(3)] CurrencyType CurrencyType,
    [property: Id(4)] DateTime CreatedAt,
    [property: Id(5)] DateTime? ExpiresAt);
