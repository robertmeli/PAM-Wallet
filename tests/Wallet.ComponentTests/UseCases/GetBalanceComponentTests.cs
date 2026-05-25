using FluentAssertions;
using Wallet.Application.UseCases;
using Wallet.ComponentTests.Fakes;
using Xunit;

namespace Wallet.ComponentTests.UseCases;

public sealed class GetBalanceComponentTests
{
    private readonly InMemoryWalletRepository _repository = new();
    private readonly AddFundsHandler _addHandler;
    private readonly GetBalanceHandler _balanceHandler;

    public GetBalanceComponentTests()
    {
        _addHandler = new AddFundsHandler(_repository);
        _balanceHandler = new GetBalanceHandler(_repository);
    }

    [Fact]
    public async Task GetBalance_ExistingWallet_ReturnsCorrectBalance()
    {
        await _addHandler.Handle("player-b1", 250m);

        var result = await _balanceHandler.Handle("player-b1");

        result.Should().NotBeNull();
        result!.PlayerId.Should().Be("player-b1");
        result.Balance.Should().Be(250m);
    }

    [Fact]
    public async Task GetBalance_WalletNotFound_ReturnsNull()
    {
        var result = await _balanceHandler.Handle("nonexistent");

        result.Should().BeNull();
    }
}
