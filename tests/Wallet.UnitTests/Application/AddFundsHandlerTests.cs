using FluentAssertions;
using NSubstitute;
using Wallet.Application.Ports;
using Wallet.Application.UseCases;
using Wallet.Domain.Enums;
using Xunit;

namespace Wallet.UnitTests.Application;

public sealed class AddFundsHandlerTests
{
    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();

    private readonly AddFundsHandler _sut;

    public AddFundsHandlerTests() =>
        _sut = new AddFundsHandler(_repository);

    [Fact]
    public async Task Handle_NewPlayer_CreatesWalletAndAddsBalance()
    {
        _repository.Get("player-1", Arg.Any<CancellationToken>()).Returns((WalletAggregate?)null);

        var result = await _sut.Handle("player-1", 100m);

        result.Success.Should().BeTrue();
        result.BalanceAfter.Should().Be(100m);
        await _repository.Received(1).SaveWithOutbox(
            Arg.Is<WalletAggregate>(w => w.PlayerId == "player-1" && w.Balance == 100m),
            Arg.Any<Wallet.Domain.Events.WalletEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingWallet_AccumulatesBalance()
    {
        var wallet = WalletAggregate.Create("player-1", WalletType.Main, CurrencyType.EUR, null);
        wallet.AddFunds(50m);
        _repository.Get("player-1", Arg.Any<CancellationToken>()).Returns(wallet);

        var result = await _sut.Handle("player-1", 100m);


        result.Success.Should().BeTrue();
        result.BalanceAfter.Should().Be(150m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task Handle_InvalidAmount_ReturnsFailure(decimal amount)
    {
        var result = await _sut.Handle("player-1", amount);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("INVALID_AMOUNT");
        await _repository.DidNotReceive().SaveWithOutbox(Arg.Any<WalletAggregate>(), Arg.Any<Wallet.Domain.Events.WalletEvent>(), Arg.Any<CancellationToken>());
    }
}
