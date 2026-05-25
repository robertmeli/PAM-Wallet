using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wallet.Infrastructure.Messaging.Kafka;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaEventPublisher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KafkaSettings>(o =>
        {
            o.BootstrapServers = configuration["Kafka:BootstrapServers"] ?? o.BootstrapServers;
            o.Topic = configuration["Kafka:Topic"] ?? o.Topic;
            o.Acks = configuration["Kafka:Acks"] ?? o.Acks;
            if (int.TryParse(configuration["Kafka:LingerMs"], out var linger)) o.LingerMs = linger;
            if (int.TryParse(configuration["Kafka:BatchSize"], out var batch)) o.BatchSize = batch;
            o.CompressionType = configuration["Kafka:CompressionType"] ?? o.CompressionType;
            if (int.TryParse(configuration["Kafka:MessageTimeoutMs"], out var timeout)) o.MessageTimeoutMs = timeout;
        });

        services.Configure<WalletEventPublishingOptions>(o =>
        {
            if (bool.TryParse(configuration["WalletEventPublishing:SkipPublishing"], out var skip)) o.SkipPublishing = skip;
            if (int.TryParse(configuration["WalletEventPublishing:QueueCapacity"], out var cap)) o.QueueCapacity = cap;
            if (int.TryParse(configuration["WalletEventPublishing:WorkerCount"], out var workers)) o.WorkerCount = workers;
            if (int.TryParse(configuration["WalletEventPublishing:OutboxBatchSize"], out var outboxBatch)) o.OutboxBatchSize = outboxBatch;
            if (int.TryParse(configuration["WalletEventPublishing:OutboxPollIntervalMs"], out var poll)) o.OutboxPollIntervalMs = poll;
            if (int.TryParse(configuration["WalletEventPublishing:OutboxBusyPollIntervalMs"], out var busyPoll)) o.OutboxBusyPollIntervalMs = busyPoll;
            if (int.TryParse(configuration["WalletEventPublishing:OutboxMaxBatchesPerCycle"], out var maxBatches)) o.OutboxMaxBatchesPerCycle = maxBatches;
        });
        services.AddSingleton<KafkaWalletEventPublisher>();
        return services;
    }
}
