using FluentAssertions;
using Wallet.Application.UseCases;
using Wallet.ComponentTests.Fakes;
using Wallet.Domain.Common;
using Xunit;

namespace Wallet.ComponentTests.UseCases;

public sealed class DeductFundsComponentTests
{
    private readonly InMemoryWalletRepository _repository = new();
    private readonly AddFundsHandler _addHandler;
    private readonly DeductFundsHandler _deductHandler;

    public DeductFundsComponentTests()
    {
        _addHandler = new AddFundsHandler(_repository);
        _deductHandler = new DeductFundsHandler(_repository);
    }

    [Fact]
    public async Task DeductFunds_WithSufficientBalance_Succeeds()
    {
        await _addHandler.Handle("player-d1", 100m);

        var result = await _deductHandler.Handle("player-d1", 60m);

        result.Success.Should().BeTrue();
        result.BalanceAfter.Should().Be(40m);
    }

    [Fact]
    public async Task DeductFunds_InsufficientFunds_ReturnsFalseAndPublishesRejected()
    {
        await _addHandler.Handle("player-d2", 30m);

        var result = await _deductHandler.Handle("player-d2", 100m);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(DomainErrors.InsufficientFunds);

        var wallet = await _repository.Get("player-d2");
        wallet!.Balance.Should().Be(30m);
    }

    [Fact]
    public async Task DeductFunds_WalletNotFound_ReturnsFalse()
    {
        var result = await _deductHandler.Handle("nonexistent", 10m);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("WALLET_NOT_FOUND");
    }

    [Fact]
    public async Task DeductFunds_ExactBalance_SetsBalanceToZero()
    {
        await _addHandler.Handle("player-d3", 100m);
        var result = await _deductHandler.Handle("player-d3", 100m);

        result.Success.Should().BeTrue();
        result.BalanceAfter.Should().Be(0m);
    }
}
