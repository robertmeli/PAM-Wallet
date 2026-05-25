using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application.UseCases;
using Wallet.Domain.Common;
using Wallet.Domain.Events;
using Wallet.Infrastructure.Persistence.Postgres;
using Wallet.IntegrationTests.Fixtures;
using Xunit;

namespace Wallet.IntegrationTests.Persistence;

[Collection("Postgres")]
public sealed class WalletReconciliationIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AddAndDeduct_ReconcilesFinalBalanceAndOutboxEvents()
    {
        var dbFactory = fixture.CreateDbContextFactory();
        var repository = new PostgresWalletRepository(dbFactory, NullLogger<PostgresWalletRepository>.Instance);
        var addFunds = new AddFundsHandler(repository);
        var deductFunds = new DeductFundsHandler(repository);

        var playerId = $"pg-reconcile-{Guid.NewGuid():N}";

        var add1 = await addFunds.Handle(playerId, 100m);
        var add2 = await addFunds.Handle(playerId, 40m);
        var deduct1 = await deductFunds.Handle(playerId, 50m);
        var deductRejected = await deductFunds.Handle(playerId, 200m);

        add1.Success.Should().BeTrue();
        add2.Success.Should().BeTrue();
        deduct1.Success.Should().BeTrue();
        deductRejected.Success.Should().BeFalse();
        deductRejected.Error.Should().Be(DomainErrors.InsufficientFunds);

        var wallet = await repository.Get(playerId);
        wallet.Should().NotBeNull();

        wallet!.Balance.Should().Be(90m, "100 + 40 - 50 = 90 and rejected deduction must not change balance");
        wallet.Version.Should().Be(3, "only successful operations increment wallet version");

        await using var db = fixture.CreateDbContext();
        var eventTypes = await db.OutboxEvents
            .AsNoTracking()
            .Where(x => x.PlayerId == playerId)
            .Select(x => x.EventType)
            .ToListAsync();

        eventTypes.Count.Should().Be(4);
        eventTypes.Should().Contain(WalletEventType.FundsAdded.ToString());
        eventTypes.Should().Contain(WalletEventType.FundsDeducted.ToString());
        eventTypes.Should().Contain(WalletEventType.FundsDeductionRejected.ToString());
    }

    [Fact]
    public async Task ConcurrentAddAndDeduct_SamePlayer_ReconcilesFinalBalanceVersionAndOutbox()
    {
        var dbFactory = fixture.CreateDbContextFactory();
        var repository = new PostgresWalletRepository(dbFactory, NullLogger<PostgresWalletRepository>.Instance);
        var addFunds = new AddFundsHandler(repository);
        var deductFunds = new DeductFundsHandler(repository);

        var playerId = $"pg-reconcile-concurrent-{Guid.NewGuid():N}";

        var seed = await addFunds.Handle(playerId, 1000m);
        seed.Success.Should().BeTrue();

        const int addOps = 20;
        const int deductOps = 20;
        const decimal addAmount = 7m;
        const decimal deductAmount = 3m;

        var addTasks = Enumerable.Range(0, addOps)
            .Select(_ => ExecuteWithConcurrencyRetry(() => addFunds.Handle(playerId, addAmount)));
        var deductTasks = Enumerable.Range(0, deductOps)
            .Select(_ => ExecuteWithConcurrencyRetry(() => deductFunds.Handle(playerId, deductAmount)));

        var results = await Task.WhenAll(addTasks.Concat(deductTasks));
        results.Should().OnlyContain(r => r.Success);

        var wallet = await repository.Get(playerId);
        wallet.Should().NotBeNull();

        var expectedBalance = 1000m + (addOps * addAmount) - (deductOps * deductAmount);
        wallet!.Balance.Should().Be(expectedBalance);
        wallet.Version.Should().Be(1 + addOps + deductOps, "seed add and all successful operations increment version");

        await using var db = fixture.CreateDbContext();
        var outboxEvents = await db.OutboxEvents
            .AsNoTracking()
            .Where(x => x.PlayerId == playerId)
            .ToListAsync();

        outboxEvents.Should().HaveCount(1 + addOps + deductOps);
        outboxEvents.Should().NotContain(x => x.EventType == WalletEventType.FundsDeductionRejected.ToString());
    }

    [Fact]
    public async Task ConcurrentDeduct_InsufficientFunds_ReconcilesExactSuccessAndRejectionCounts()
    {
        var dbFactory = fixture.CreateDbContextFactory();
        var repository = new PostgresWalletRepository(dbFactory, NullLogger<PostgresWalletRepository>.Instance);
        var addFunds = new AddFundsHandler(repository);
        var deductFunds = new DeductFundsHandler(repository);

        var playerId = $"pg-reconcile-rejected-{Guid.NewGuid():N}";

        const decimal seedAmount = 100m;
        const int deductOps = 40;
        const decimal deductAmount = 5m;
        const int expectedSuccessfulDeducts = 20;
        const int expectedRejectedDeducts = deductOps - expectedSuccessfulDeducts;

        var seed = await addFunds.Handle(playerId, seedAmount);
        seed.Success.Should().BeTrue();

        var tasks = Enumerable.Range(0, deductOps)
            .Select(_ => ExecuteWithConcurrencyRetry(() => deductFunds.Handle(playerId, deductAmount)));

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r.Success);
        var rejectedCount = results.Count(r => !r.Success && r.Error == DomainErrors.InsufficientFunds);

        successCount.Should().Be(expectedSuccessfulDeducts);
        rejectedCount.Should().Be(expectedRejectedDeducts);

        var wallet = await repository.Get(playerId);
        wallet.Should().NotBeNull();
        wallet!.Balance.Should().Be(0m);
        wallet.Version.Should().Be(1 + expectedSuccessfulDeducts, "seed add and successful deductions increment version");

        await using var db = fixture.CreateDbContext();
        var outboxEvents = await db.OutboxEvents
            .AsNoTracking()
            .Where(x => x.PlayerId == playerId)
            .ToListAsync();

        outboxEvents.Should().HaveCount(1 + deductOps);
        outboxEvents.Count(x => x.EventType == WalletEventType.FundsAdded.ToString()).Should().Be(1);
        outboxEvents.Count(x => x.EventType == WalletEventType.FundsDeducted.ToString()).Should().Be(expectedSuccessfulDeducts);
        outboxEvents.Count(x => x.EventType == WalletEventType.FundsDeductionRejected.ToString()).Should().Be(expectedRejectedDeducts);
    }

    private static async Task<Wallet.Application.DTOs.WalletOperationResult> ExecuteWithConcurrencyRetry(
        Func<Task<Wallet.Application.DTOs.WalletOperationResult>> action,
        int maxAttempts = 20)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException" && attempt < maxAttempts)
            {
                await Task.Delay(10 * attempt);
            }
        }

        throw new InvalidOperationException("Exceeded retry attempts for concurrent wallet operation.");
    }
}
