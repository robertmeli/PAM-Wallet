using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System.Diagnostics;
using Wallet.Application.DTOs;
using Wallet.Application.Ports;
using Wallet.Application.UseCases;
using Wallet.Domain.Enums;
using WalletAggregate = Wallet.Domain.Aggregates.Wallet;

namespace Wallet.Infrastructure.Orchestration.Orleans;

public sealed class WalletGrain(
    CreateWalletHandler createWalletHandler,
    AddFundsHandler addFundsHandler,
    DeductFundsHandler deductFundsHandler,
    GetBalanceHandler getBalanceHandler,
    IWalletRepository repository,
    [PersistentState("wallet", "Default")] IPersistentState<WalletGrainState> state,
    ILogger<WalletGrain> logger) : Grain, IWalletGrain
{
    private long _addFundsConcurrencyRetries;
    private long _deductFundsConcurrencyRetries;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var wallet = await repository.Get(this.GetPrimaryKeyString(), cancellationToken);
        if (wallet is null)
        {
            state.State.IsLoaded = false;
        }
        else
        {
            SyncStateFromAggregate(wallet);
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<WalletGrainBalanceResult?> CreateWallet(WalletType walletType = WalletType.Main,
        CurrencyType currencyType = CurrencyType.EUR, DateTime? expiresAt = null)
    {
        var result = await createWalletHandler.Handle(this.GetPrimaryKeyString(), walletType, currencyType, expiresAt);
        if (result is null)
            return null;

        await RefreshStateAsync(this.GetPrimaryKeyString());

        return new WalletGrainBalanceResult(
            result.PlayerId, result.Balance, result.WalletType, result.CurrencyType,
            result.CreatedAt, result.ExpiresAt);
    }

    public async Task<WalletGrainOperationResult> AddFunds(decimal amount, WalletType walletType = WalletType.Main,
        CurrencyType currencyType = CurrencyType.EUR, DateTime? expiresAt = null)
    {
        var playerId = this.GetPrimaryKeyString();
        var diagnosticsEnabled = logger.IsEnabled(LogLevel.Debug);
        var startedAt = diagnosticsEnabled ? Stopwatch.GetTimestamp() : 0;

        long ElapsedMs() => diagnosticsEnabled
            ? (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
            : 0;

        logger.LogDebug("grain add_funds start player {PlayerId} amount {Amount}", playerId, amount);

        try
        {
            var result = await ExecuteWithConcurrencyRetry(
                () => addFundsHandler.Handle(playerId, amount, walletType, currencyType, expiresAt),
                () => Interlocked.Increment(ref _addFundsConcurrencyRetries),
                "add_funds",
                playerId);

            if (result.Success)
            {
                if (state.State.IsLoaded)
                {
                    ApplySuccessfulOperationResultToLoadedState(result);
                }
                else
                {
                    await RefreshStateAsync(playerId);
                }
            }

            logger.LogDebug("grain add_funds end player {PlayerId} success {Success} elapsedMs {ElapsedMs}",
                playerId, result.Success, ElapsedMs());

            return ToGrainResult(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "grain add_funds failed player {PlayerId} elapsedMs {ElapsedMs}", playerId, ElapsedMs());
            throw;
        }
    }

    public async Task<WalletGrainOperationResult> DeductFunds(decimal amount)
    {
        var playerId = this.GetPrimaryKeyString();

        var result = await ExecuteWithConcurrencyRetry(
            () => deductFundsHandler.Handle(playerId, amount),
            () => Interlocked.Increment(ref _deductFundsConcurrencyRetries),
            "deduct_funds",
            playerId);

        if (result.Success)
        {
            if (state.State.IsLoaded)
            {
                ApplySuccessfulOperationResultToLoadedState(result);
            }
            else
            {
                await RefreshStateAsync(playerId);
            }
        }

        return ToGrainResult(result);
    }

    public async Task<WalletGrainBalanceResult?> GetBalance()
    {
        if (state.State.IsLoaded)
        {
            return new WalletGrainBalanceResult(
                this.GetPrimaryKeyString(),
                state.State.Balance,
                state.State.WalletType,
                state.State.CurrencyType,
                state.State.CreatedAt,
                state.State.ExpiresAt);
        }

        var balance = await getBalanceHandler.Handle(this.GetPrimaryKeyString());
        if (balance is null)
            return null;

        SyncStateFromBalanceSnapshot(balance);

        return new WalletGrainBalanceResult(
            balance.PlayerId, balance.Balance, balance.WalletType, balance.CurrencyType,
            balance.CreatedAt, balance.ExpiresAt);
    }

    private async Task RefreshStateAsync(string playerId, CancellationToken ct = default)
    {
        var wallet = await repository.Get(playerId, ct);
        if (wallet is null)
        {
            state.State.IsLoaded = false;
            return;
        }

        SyncStateFromAggregate(wallet);
    }

    private async Task<WalletOperationResult> ExecuteWithConcurrencyRetry(
        Func<Task<WalletOperationResult>> action,
        Func<long> incrementCounter,
        string operation,
        string playerId,
        int maxAttempts = 2)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxAttempts && ex.GetType().Name == "DbUpdateConcurrencyException")
            {
                var retries = incrementCounter();
                if (retries % 100 == 0)
                {
                    logger.LogWarning(
                        "grain {Operation} concurrency conflicts reached {RetryCount}. Latest player {PlayerId}",
                        operation,
                        retries,
                        playerId);
                }
                else
                {
                    logger.LogDebug(
                        "grain {Operation} concurrency conflict player {PlayerId}; retry {RetryCount}",
                        operation,
                        playerId,
                        retries);
                }
            }
        }

        throw new InvalidOperationException($"Exceeded retry attempts for grain operation '{operation}' on player '{playerId}'.");
    }

    private static WalletGrainOperationResult ToGrainResult(WalletOperationResult result) =>
        new(result.Success, result.Amount, result.BalanceBefore, result.BalanceAfter, result.Error);

    private void ApplySuccessfulOperationResultToLoadedState(WalletOperationResult result)
    {
        if (!result.BalanceAfter.HasValue)
            return;

        state.State.IsLoaded = true;
        state.State.Balance = result.BalanceAfter.Value;
        state.State.Version += 1;
        state.State.UpdatedAtUtc = DateTime.UtcNow;
    }

    private void SyncStateFromBalanceSnapshot(BalanceResult balance)
    {
        state.State.IsLoaded = true;
        state.State.Balance = balance.Balance;
        state.State.WalletType = balance.WalletType;
        state.State.CurrencyType = balance.CurrencyType;
        state.State.CreatedAt = balance.CreatedAt;
        state.State.ExpiresAt = balance.ExpiresAt;

        if (state.State.UpdatedAtUtc == default)
        {
            state.State.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private void SyncStateFromAggregate(WalletAggregate wallet)
    {
        state.State.IsLoaded = true;
        state.State.Balance = wallet.Balance;
        state.State.Version = wallet.Version;
        state.State.UpdatedAtUtc = wallet.UpdatedAtUtc;
        state.State.WalletType = wallet.WalletType;
        state.State.CurrencyType = wallet.CurrencyType;
        state.State.CreatedAt = wallet.CreatedAt;
        state.State.ExpiresAt = wallet.ExpiresAt;
    }
}
