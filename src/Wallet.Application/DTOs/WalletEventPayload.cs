using System.Text.Json.Serialization;
using Wallet.Domain.Events;

namespace Wallet.Application.DTOs;

public sealed record WalletEventPayload(
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("playerId")] string PlayerId,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("balanceAfter")] decimal BalanceAfter,
    [property: JsonPropertyName("occurredAtUtc")] DateTime OccurredAtUtc,
    [property: JsonPropertyName("version")] long Version)
{
    public static WalletEventPayload FromWalletEvent(WalletEvent walletEvent) =>
        new(
            walletEvent.EventId,
            walletEvent.PlayerId,
            walletEvent.EventType.ToString(),
            walletEvent.Amount,
            walletEvent.BalanceAfter,
            walletEvent.OccurredAtUtc,
            walletEvent.Version);
}
