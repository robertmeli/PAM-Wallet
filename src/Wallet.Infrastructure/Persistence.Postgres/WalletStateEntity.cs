using Wallet.Domain.Enums;

namespace Wallet.Infrastructure.Persistence.Postgres;

public sealed class WalletStateEntity
{
    public string PlayerId { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public long Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public WalletType WalletType { get; set; }
    public CurrencyType CurrencyType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
