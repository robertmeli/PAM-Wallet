using FluentAssertions;
using NSubstitute;
using Wallet.Application.Ports;
using Wallet.Application.UseCases;
using Xunit;

namespace Wallet.UnitTests.Application;

public sealed class GetBalanceHandlerTests
{
    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();
    private readonly GetBalanceHandler _sut;

    public GetBalanceHandlerTests() =>
        _sut = new GetBalanceHandler(_repository);

    [Fact]
    public async Task Handle_ExistingWallet_ReturnsBalance()
    {
        var wallet = WalletAggregate.Create("player-1");
        wallet.AddFunds(75m);
        _repository.Get("player-1").Returns(wallet);

        var result = await _sut.Handle("player-1");

        result.Should().NotBeNull();
        result!.PlayerId.Should().Be("player-1");
        result.Balance.Should().Be(75m);
    }

    [Fact]
    public async Task Handle_WalletNotFound_ReturnsNull()
    {
        _repository.Get("unknown").Returns((WalletAggregate?)null);

        var result = await _sut.Handle("unknown");

        result.Should().BeNull();
    }
}
