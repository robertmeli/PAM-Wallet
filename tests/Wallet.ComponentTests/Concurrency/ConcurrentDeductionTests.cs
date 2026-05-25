using FluentAssertions;
using Wallet.Application.UseCases;
using Wallet.ComponentTests.Fakes;
using Xunit;

namespace Wallet.ComponentTests.Concurrency;

/// <summary>
/// PRD §16 concurrency test:
/// Initial balance = 100, 10 concurrent deductions of 20 → 5 succeed, 5 fail, final balance = 0.
/// 
/// NOTE: InMemoryWalletRepository does NOT enforce single-writer semantics.
/// This test validates domain correctness through Orleans or a locking wrapper.
/// Use Wallet.IntegrationTests for full concurrency guarantees with PostgreSQL.
/// This suite verifies the domain logic outcome using a thread-safe in-memory store.
/// </summary>
public sealed class ConcurrentDeductionTests
{
    [Fact]
    public async Task TenConcurrentDeductionsOf20_StartingAt100_FiveSucceedFiveFail()
    {
        var repository = new ThreadSafeInMemoryWalletRepository();

        var addHandler = new AddFundsHandler(repository);
        await addHandler.Handle("player-concurrent", 100m);

        var deductHandler = new DeductFundsHandler(repository);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => deductHandler.Handle("player-concurrent", 20m))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        successCount.Should().Be(5, "exactly 5 deductions of 20 should succeed from a balance of 100");
        failureCount.Should().Be(5);

        var wallet = await repository.Get("player-concurrent");
        wallet!.Balance.Should().Be(0m, "final balance must be zero after 5 successful deductions");
    }
}
