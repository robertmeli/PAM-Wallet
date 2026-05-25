using FluentAssertions;
using Wallet.Domain.Common;
using Wallet.Domain.Events;
using Xunit;

namespace Wallet.UnitTests.Domain;

public sealed class WalletAggregateTests
{
    [Fact]
    public void Create_ShouldInitialise_WithZeroBalance()
    {
        var wallet = WalletAggregate.Create("player-1");

        wallet.PlayerId.Should().Be("player-1");
        wallet.Balance.Should().Be(0m);
        wallet.Version.Should().Be(0);
    }

    [Fact]
    public void AddFunds_WithPositiveAmount_ShouldIncreaseBalance()
    {
        var wallet = WalletAggregate.Create("player-1");

        var result = wallet.AddFunds(100m);

        result.IsSuccess.Should().BeTrue();
        wallet.Balance.Should().Be(100m);
        wallet.Version.Should().Be(1);
        result.Value!.EventType.Should().Be(WalletEventType.FundsAdded);
        result.Value.Amount.Should().Be(100m);
        result.Value.BalanceAfter.Should().Be(100m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.5)]
    public void AddFunds_WithNonPositiveAmount_ShouldReturnFailure(decimal amount)
    {
        var wallet = WalletAggregate.Create("player-1");

        var result = wallet.AddFunds(amount);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DomainErrors.InvalidAmount);
        wallet.Balance.Should().Be(0m);
        wallet.Version.Should().Be(0);
    }

    [Fact]
    public void DeductFunds_WithSufficientBalance_ShouldDecreaseBalance()
    {
        var wallet = WalletAggregate.Create("player-1");
        wallet.AddFunds(100m);

        var result = wallet.DeductFunds(60m);

        result.IsSuccess.Should().BeTrue();
        wallet.Balance.Should().Be(40m);
        wallet.Version.Should().Be(2);
        result.Value!.EventType.Should().Be(WalletEventType.FundsDeducted);
        result.Value.Amount.Should().Be(60m);
        result.Value.BalanceAfter.Should().Be(40m);
    }

    [Fact]
    public void DeductFunds_WithInsufficientBalance_ShouldReturnFailure()
    {
        var wallet = WalletAggregate.Create("player-1");
        wallet.AddFunds(50m);

        var result = wallet.DeductFunds(100m);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DomainErrors.InsufficientFunds);
        wallet.Balance.Should().Be(50m);
        wallet.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void DeductFunds_WithNonPositiveAmount_ShouldReturnFailure(decimal amount)
    {
        var wallet = WalletAggregate.Create("player-1");
        wallet.AddFunds(100m);

        var result = wallet.DeductFunds(amount);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DomainErrors.InvalidAmount);
    }

    [Fact]
    public void DeductFunds_WithExactBalance_ShouldReduceToZero()
    {
        var wallet = WalletAggregate.Create("player-1");
        wallet.AddFunds(100m);

        var result = wallet.DeductFunds(100m);

        result.IsSuccess.Should().BeTrue();
        wallet.Balance.Should().Be(0m);
    }

    [Fact]
    public void AddFunds_MultipleTimes_ShouldAccumulateBalance()
    {
        var wallet = WalletAggregate.Create("player-1");

        wallet.AddFunds(100m);
        wallet.AddFunds(50m);
        wallet.AddFunds(25m);

        wallet.Balance.Should().Be(175m);
        wallet.Version.Should().Be(3);
    }

    [Fact]
    public void AddFunds_ShouldReturn_EventWithCorrectPlayerId()
    {
        var wallet = WalletAggregate.Create("player-xyz");

        var result = wallet.AddFunds(200m);

        result.Value!.PlayerId.Should().Be("player-xyz");
        result.Value.EventId.Should().NotBeNullOrWhiteSpace();
    }
}
