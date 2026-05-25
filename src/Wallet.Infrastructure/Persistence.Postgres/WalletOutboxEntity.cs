namespace Wallet.Infrastructure.Persistence.Postgres;

public sealed class WalletOutboxEntity
{
    public string EventId { get; set; } = default!;
    public string PlayerId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long Version { get; set; }
    public string Payload { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
}
