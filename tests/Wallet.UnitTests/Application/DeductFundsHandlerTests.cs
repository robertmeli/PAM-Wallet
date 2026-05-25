using FluentAssertions;
using NSubstitute;
using Wallet.Application.Ports;
using Wallet.Application.UseCases;
using Wallet.Domain.Common;
using Xunit;

namespace Wallet.UnitTests.Application;

public sealed class DeductFundsHandlerTests
{
    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();
    private readonly DeductFundsHandler _sut;

    public DeductFundsHandlerTests() =>
        _sut = new DeductFundsHandler(_repository);

    [Fact]
    public async Task Handle_SufficientBalance_DeductsAndPublishesEvent()
    {
        var wallet = WalletAggregate.Create("player-1");
        wallet.AddFunds(100m);
        _repository.Get("player-1").Returns(wallet);

        var result = await _sut.Handle("player-1", 40m);

        result.Success.Should().BeTrue();
        result.BalanceAfter.Should().Be(60m);
        await _repository.Received(1).SaveWithOutbox(
            Arg.Any<WalletAggregate>(),
            Arg.Is<Wallet.Domain.Events.WalletEvent>(e => e.EventType == Wallet.Domain.Events.WalletEventType.FundsDeducted && e.Amount == 40m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InsufficientFunds_ReturnsFailureAndPublishesRejected()
    {
        var wallet = WalletAggregate.Create("player-1");
        wallet.AddFunds(30m);
        _repository.Get("player-1").Returns(wallet);

        var result = await _sut.Handle("player-1", 100m);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(DomainErrors.InsufficientFunds);
        await _repository.DidNotReceive().SaveWithOutbox(Arg.Any<WalletAggregate>(), Arg.Any<Wallet.Domain.Events.WalletEvent>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).AppendOutboxEvent(Arg.Is<Wallet.Domain.Events.WalletEvent>(e =>
            e.EventType == Wallet.Domain.Events.WalletEventType.FundsDeductionRejected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WalletNotFound_ReturnsFailure()
    {
        _repository.Get("unknown").Returns((WalletAggregate?)null);

        var result = await _sut.Handle("unknown", 50m);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("WALLET_NOT_FOUND");
        await _repository.DidNotReceive().SaveWithOutbox(Arg.Any<WalletAggregate>(), Arg.Any<Wallet.Domain.Events.WalletEvent>(), Arg.Any<CancellationToken>());
    }
}
