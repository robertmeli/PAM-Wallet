using Wallet.Domain.Enums;
using Wallet.Api.Serialization;
using System.Text.Json.Serialization;

namespace Wallet.Api.Contracts;

public sealed record AddFundsRequest(
    decimal Amount,
    WalletType WalletType = WalletType.Main,
    CurrencyType CurrencyType = CurrencyType.EUR,
    [property: JsonConverter(typeof(FlexibleNullableDateTimeConverter))] DateTime? ExpiresAt = null);
