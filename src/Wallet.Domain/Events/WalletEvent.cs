namespace Wallet.Domain.Events;

public sealed record WalletEvent(
    string EventId,
    string PlayerId,
    WalletEventType EventType,
    decimal Amount,
    decimal BalanceAfter,
    DateTime OccurredAtUtc,
    long Version);

public enum WalletEventType
{
    FundsAdded,
    FundsDeducted,
    FundsDeductionRejected
}
