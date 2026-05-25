using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wallet.Application.DTOs;
using Wallet.Domain.Events;

namespace Wallet.Infrastructure.Messaging.Kafka;

public sealed class KafkaWalletEventPublisher : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaSettings _settings;
    private readonly ILogger<KafkaWalletEventPublisher> _logger;

    public KafkaWalletEventPublisher(IOptions<KafkaSettings> settings, ILogger<KafkaWalletEventPublisher> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var acks = _settings.Acks.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? Acks.All
            : Acks.Leader;
        var compressionType = _settings.CompressionType.Equals("Lz4", StringComparison.OrdinalIgnoreCase)
            ? CompressionType.Lz4
            : CompressionType.Snappy;

        var config = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Plaintext,
            Acks = acks,
            LingerMs = _settings.LingerMs,
            BatchSize = _settings.BatchSize,
            CompressionType = compressionType,
            MessageTimeoutMs = _settings.MessageTimeoutMs,
            EnableIdempotence = false
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public Task Publish(WalletEvent walletEvent, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(WalletEventPayload.FromWalletEvent(walletEvent));

        try
        {
            _producer.Produce(
                _settings.Topic,
                new Message<string, string> { Key = walletEvent.PlayerId, Value = payload },
                report =>
                {
                    if (report.Error.IsError)
                    {
                        _logger.LogError("Failed to publish wallet event {EventType} for player {PlayerId}: {Reason}",
                            walletEvent.EventType, walletEvent.PlayerId, report.Error.Reason);
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish wallet event {EventType} for player {PlayerId}",
                walletEvent.EventType, walletEvent.PlayerId);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _producer.Dispose();
}
