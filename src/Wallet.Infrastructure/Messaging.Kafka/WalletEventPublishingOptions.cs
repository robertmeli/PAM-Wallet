namespace Wallet.Infrastructure.Messaging.Kafka;

public sealed class WalletEventPublishingOptions
{
    public bool SkipPublishing { get; set; }
    public int QueueCapacity { get; set; } = 100_000;
    public int WorkerCount { get; set; } = 4;
    public int OutboxBatchSize { get; set; } = 200;
    public int OutboxPollIntervalMs { get; set; } = 500;
    public int OutboxBusyPollIntervalMs { get; set; } = 100;
    public int OutboxMaxBatchesPerCycle { get; set; } = 1;
}
