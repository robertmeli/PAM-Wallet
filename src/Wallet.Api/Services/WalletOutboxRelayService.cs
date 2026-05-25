using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.Domain.Events;
using Wallet.Infrastructure.Messaging.Kafka;
using Wallet.Infrastructure.Persistence.Postgres;

namespace Wallet.Api.Services;

public sealed class WalletOutboxRelayService(
    IDbContextFactory<WalletDbContext> dbContextFactory,
    KafkaWalletEventPublisher kafkaPublisher,
    IOptions<WalletEventPublishingOptions> options,
    ILogger<WalletOutboxRelayService> logger) : BackgroundService
{
    private readonly WalletEventPublishingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idlePollIntervalMs = _options.OutboxPollIntervalMs <= 0 ? 500 : _options.OutboxPollIntervalMs;
        var busyPollIntervalMs = _options.OutboxBusyPollIntervalMs <= 0 ? 100 : _options.OutboxBusyPollIntervalMs;
        var batchSize = _options.OutboxBatchSize <= 0 ? 200 : _options.OutboxBatchSize;
        var maxBatchesPerCycle = _options.OutboxMaxBatchesPerCycle <= 0 ? 1 : _options.OutboxMaxBatchesPerCycle;

        logger.LogInformation(
            "Wallet outbox relay started with batch size {BatchSize}, idle poll {IdlePollMs}ms, busy poll {BusyPollMs}ms, max batches/cycle {MaxBatchesPerCycle}, skip publishing {SkipPublishing}",
            batchSize,
            idlePollIntervalMs,
            busyPollIntervalMs,
            maxBatchesPerCycle,
            _options.SkipPublishing);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.SkipPublishing)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(idlePollIntervalMs), stoppingToken);
                    continue;
                }

                var processedInCycle = 0;

                for (var i = 0; i < maxBatchesPerCycle; i++)
                {
                    var processed = await ProcessBatchAsync(batchSize, stoppingToken);
                    processedInCycle += processed;

                    if (processed < batchSize)
                        break;
                }

                var delayMs = processedInCycle == 0 ? idlePollIntervalMs : busyPollIntervalMs;
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Wallet outbox relay loop failed; retrying after delay");
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(500, idlePollIntervalMs)), stoppingToken);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        await using var strategyDb = await dbContextFactory.CreateDbContextAsync(ct);
        var strategy = strategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var outboxRows = await db.OutboxEvents
                .FromSqlRaw(@"
                SELECT event_id, player_id, event_type, amount, balance_after, occurred_at_utc, version, payload, created_at_utc
                FROM wallet_outbox
                ORDER BY created_at_utc
                LIMIT {0}
                FOR UPDATE SKIP LOCKED", batchSize)
                .ToListAsync(ct);

            if (outboxRows.Count == 0)
            {
                await tx.CommitAsync(ct);
                return 0;
            }

            foreach (var row in outboxRows)
            {
                if (!Enum.TryParse<WalletEventType>(row.EventType, ignoreCase: true, out var eventType))
                {
                    logger.LogWarning(
                        "Skipping outbox event {EventId} because event type '{EventType}' is invalid",
                        row.EventId,
                        row.EventType);
                    db.OutboxEvents.Remove(row);
                    continue;
                }

                var walletEvent = new WalletEvent(
                    row.EventId,
                    row.PlayerId,
                    eventType,
                    row.Amount,
                    row.BalanceAfter,
                    row.OccurredAtUtc,
                    row.Version);

                await kafkaPublisher.Publish(walletEvent, ct);
                db.OutboxEvents.Remove(row);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return outboxRows.Count;
        });
    }
}
