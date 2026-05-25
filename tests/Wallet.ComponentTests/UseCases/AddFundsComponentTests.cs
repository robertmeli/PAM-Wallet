using FluentAssertions;
using Wallet.Application.UseCases;
using Wallet.ComponentTests.Fakes;
using Xunit;

namespace Wallet.ComponentTests.UseCases;

public sealed class AddFundsComponentTests
{
    private readonly InMemoryWalletRepository _repository = new();
    private readonly AddFundsHandler _handler;

    public AddFundsComponentTests() =>
        _handler = new AddFundsHandler(_repository);

    [Fact]
    public async Task AddFunds_NewPlayer_CreatesWalletWithBalance()
    {
        var result = await _handler.Handle("player-comp-1", 200m);

        result.Success.Should().BeTrue();
        result.BalanceAfter.Should().Be(200m);

        var wallet = await _repository.Get("player-comp-1");
        wallet.Should().NotBeNull();
        wallet!.Balance.Should().Be(200m);
    }

    [Fact]
    public async Task AddFunds_TwiceForSamePlayer_AccumulatesBalance()
    {
        await _handler.Handle("player-comp-2", 100m);
        var result = await _handler.Handle("player-comp-2", 50m);

        result.Success.Should().BeTrue();
        result.BalanceAfter.Should().Be(150m);
    }

    [Fact]
    public async Task AddFunds_PersistsUpdatedWalletState()
    {
        await _handler.Handle("player-comp-3", 75m);

        var wallet = await _repository.Get("player-comp-3");
        wallet.Should().NotBeNull();
        wallet!.Balance.Should().Be(75m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task AddFunds_InvalidAmount_ReturnsFailure(decimal amount)
    {
        var result = await _handler.Handle("player-comp-4", amount);

        result.Success.Should().BeFalse();
    }
}
