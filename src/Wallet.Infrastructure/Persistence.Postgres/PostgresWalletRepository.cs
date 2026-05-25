using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Diagnostics;
using System.Text.Json;
using Wallet.Application.DTOs;
using Wallet.Application.Ports;
using Wallet.Domain.Enums;
using Wallet.Domain.Events;

namespace Wallet.Infrastructure.Persistence.Postgres;

public sealed class PostgresWalletRepository(
    IDbContextFactory<WalletDbContext> dbContextFactory,
    ILogger<PostgresWalletRepository> logger)
    : IWalletRepository
{
    public async Task<WalletAggregate?> Get(string playerId, CancellationToken ct = default)
    {
        var diagnosticsEnabled = logger.IsEnabled(LogLevel.Debug);
        var startedAt = diagnosticsEnabled ? Stopwatch.GetTimestamp() : 0;

        long ElapsedMs() => diagnosticsEnabled
            ? (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
            : 0;

        logger.LogDebug("repo get start player {PlayerId}", playerId);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.PlayerId == playerId, ct);

        logger.LogDebug(
            "repo get end player {PlayerId} found {Found} elapsedMs {ElapsedMs}",
            playerId,
            entity is not null,
            ElapsedMs());

        if (entity is null)
            return null;

        return WalletAggregate.Reconstitute(entity.PlayerId, entity.Balance, entity.Version, entity.UpdatedAtUtc,
            entity.WalletType, entity.CurrencyType, entity.CreatedAt, entity.ExpiresAt);
    }

    public async Task Save(WalletAggregate wallet, CancellationToken ct = default)
    {
        var diagnosticsEnabled = logger.IsEnabled(LogLevel.Debug);
        var startedAt = diagnosticsEnabled ? Stopwatch.GetTimestamp() : 0;

        long ElapsedMs() => diagnosticsEnabled
            ? (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
            : 0;

        logger.LogDebug("repo save start player {PlayerId} version {Version}", wallet.PlayerId, wallet.Version);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        try
        {
            var affected = await SaveWalletStateAsync(db, wallet, ct);

            logger.LogDebug(
                "repo save end player {PlayerId} affected {Affected} elapsedMs {ElapsedMs}",
                wallet.PlayerId,
                affected,
                ElapsedMs());

            if (affected == 0)
                throw new DbUpdateConcurrencyException($"Concurrency conflict saving wallet {wallet.PlayerId}");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogWarning(ex, "Duplicate insert race saving wallet {PlayerId}", wallet.PlayerId);
            throw;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict saving wallet {PlayerId}", wallet.PlayerId);
            throw;
        }
    }

    public async Task SaveWithOutbox(WalletAggregate wallet, WalletEvent walletEvent, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            var affected = await SaveWalletStateAsync(db, wallet, ct);
            if (affected == 0)
                throw new DbUpdateConcurrencyException($"Concurrency conflict saving wallet {wallet.PlayerId}");

            await InsertOutboxAsync(db, walletEvent, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task AppendOutboxEvent(WalletEvent walletEvent, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await InsertOutboxAsync(db, walletEvent, ct);
    }

    private static Task<int> SaveWalletStateAsync(WalletDbContext db, WalletAggregate wallet, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO wallets (player_id, balance, version, updated_at_utc, wallet_type, currency_type, created_at, expires_at)
                VALUES ({wallet.PlayerId}, {wallet.Balance}, {wallet.Version}, {wallet.UpdatedAtUtc}, {wallet.WalletType}, {wallet.CurrencyType}, {wallet.CreatedAt}, {wallet.ExpiresAt})
                ON CONFLICT (player_id) DO UPDATE
                SET balance = EXCLUDED.balance,
                    updated_at_utc = EXCLUDED.updated_at_utc,
                    version = EXCLUDED.version
                WHERE wallets.version = EXCLUDED.version - 1;", ct);

    private static Task<int> InsertOutboxAsync(WalletDbContext db, WalletEvent walletEvent, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(WalletEventPayload.FromWalletEvent(walletEvent));

        return db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO wallet_outbox
                (event_id, player_id, event_type, amount, balance_after, occurred_at_utc, version, payload, created_at_utc)
            VALUES
                ({walletEvent.EventId}, {walletEvent.PlayerId}, {walletEvent.EventType.ToString()}, {walletEvent.Amount},
                 {walletEvent.BalanceAfter}, {walletEvent.OccurredAtUtc}, {walletEvent.Version}, {payload}::jsonb, {DateTime.UtcNow});", ct);
    }

}
