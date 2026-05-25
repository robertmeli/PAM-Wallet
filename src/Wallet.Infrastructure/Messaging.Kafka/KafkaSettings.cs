namespace Wallet.Infrastructure.Messaging.Kafka;

public sealed class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "wallet-events";
    public string Acks { get; set; } = "Leader";
    public int LingerMs { get; set; } = 10;
    public int BatchSize { get; set; } = 131072;
    public string CompressionType { get; set; } = "Snappy";
    public int MessageTimeoutMs { get; set; } = 30000;
}
