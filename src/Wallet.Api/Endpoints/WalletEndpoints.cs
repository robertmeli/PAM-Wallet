using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Diagnostics;
using Wallet.Api.Contracts;
using Wallet.Infrastructure.Orchestration.Orleans;

namespace Wallet.Api.Endpoints;

public static class WalletEndpoints
{
    public static IEndpointRouteBuilder MapWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wallets/{playerId}").WithTags("Wallets");

        group.MapPost("", async (
            string playerId,
            [FromBody] CreateWalletRequest request,
            IClusterClient clusterClient,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return Results.BadRequest(new { error = "INVALID_PLAYER_ID", message = "Player ID is required." });

            var grain = clusterClient.GetGrain<IWalletGrain>(playerId);
            var result = await grain.CreateWallet(request.WalletType, request.CurrencyType, request.ExpiresAt);

            if (result is null)
                return Results.Conflict(new { error = "WALLET_ALREADY_EXISTS", message = "A wallet for this player already exists." });

            return Results.Created($"/wallets/{playerId}/balance", new
            {
                playerId = result.PlayerId,
                balance = result.Balance,
                walletType = result.WalletType,
                currencyType = result.CurrencyType,
                createdAt = result.CreatedAt,
                expiresAt = result.ExpiresAt
            });
        })
        .WithName("CreateWallet")
        .WithOpenApi();

        group.MapPost("/funds/add", async (
            string playerId,
            [FromBody] AddFundsRequest request,
            IClusterClient clusterClient,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return Results.BadRequest(new { error = "INVALID_PLAYER_ID", message = "Player ID is required." });

            var logger = loggerFactory.CreateLogger("Wallet.Api.Endpoints.AddFunds");
            var diagnosticsEnabled = logger.IsEnabled(LogLevel.Debug);
            var requestStart = diagnosticsEnabled ? Stopwatch.GetTimestamp() : 0;

            long ElapsedMs() => diagnosticsEnabled
                ? (long)Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds
                : 0;

            try
            {
                logger.LogDebug(
                    "add_funds endpoint start player {PlayerId} amount {Amount}",
                    playerId,
                    request.Amount);

                var grain = clusterClient.GetGrain<IWalletGrain>(playerId);
                logger.LogDebug(
                    "add_funds endpoint grain call start player {PlayerId} elapsedMs {ElapsedMs}",
                    playerId,
                    ElapsedMs());

                var result = await grain.AddFunds(request.Amount, request.WalletType, request.CurrencyType, request.ExpiresAt);

                logger.LogDebug(
                    "add_funds endpoint grain call end player {PlayerId} success {Success} elapsedMs {ElapsedMs}",
                    playerId,
                    result.Success,
                    ElapsedMs());

                if (!result.Success)
                    return Results.UnprocessableEntity(new { error = result.Error, message = MapErrorMessage(result.Error) });

                logger.LogDebug(
                    "add_funds endpoint completed player {PlayerId} elapsedMs {ElapsedMs}",
                    playerId,
                    ElapsedMs());

                return Results.Ok(new { success = true, operationType = "Credit", amount = result.Amount, balanceBefore = result.BalanceBefore, balanceAfter = result.BalanceAfter });
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Unhandled add_funds failure for player {PlayerId} amount {Amount} walletType {WalletType} currencyType {CurrencyType} expiresAt {ExpiresAt} elapsedMs {ElapsedMs}",
                    playerId,
                    request.Amount,
                    request.WalletType,
                    request.CurrencyType,
                    request.ExpiresAt,
                    ElapsedMs());
                throw;
            }
        })
        .WithName("AddFunds")
        .WithOpenApi();

        group.MapPost("/funds/deduct", async (
            string playerId,
            [FromBody] FundsRequest request,
            IClusterClient clusterClient,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return Results.BadRequest(new { error = "INVALID_PLAYER_ID", message = "Player ID is required." });

            var grain = clusterClient.GetGrain<IWalletGrain>(playerId);
            var result = await grain.DeductFunds(request.Amount);

            if (!result.Success)
                return Results.UnprocessableEntity(new { error = result.Error, message = MapErrorMessage(result.Error) });

            return Results.Ok(new { success = true, operationType = "Debit", amount = result.Amount, balanceBefore = result.BalanceBefore, balanceAfter = result.BalanceAfter });
        })
        .WithName("DeductFunds")
        .WithOpenApi();

        group.MapGet("/balance", async (
            string playerId,
            IClusterClient clusterClient,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return Results.BadRequest(new { error = "INVALID_PLAYER_ID", message = "Player ID is required." });

            var grain = clusterClient.GetGrain<IWalletGrain>(playerId);
            var result = await grain.GetBalance();

            if (result is null)
                return Results.NotFound(new { error = "WALLET_NOT_FOUND", message = "Wallet does not exist." });

            return Results.Ok(new
            {
                playerId = result.PlayerId,
                balance = result.Balance,
                walletType = result.WalletType,
                currencyType = result.CurrencyType,
                createdAt = result.CreatedAt,
                expiresAt = result.ExpiresAt
            });
        })
        .WithName("GetBalance")
        .WithOpenApi();

        return app;
    }

    private static string MapErrorMessage(string? error) => error switch
    {
        "INSUFFICIENT_FUNDS" => "Wallet balance is insufficient.",
        "INVALID_AMOUNT" => "Amount must be greater than zero.",
        "WALLET_NOT_FOUND" => "Wallet does not exist.",
        _ => "An unexpected error occurred."
    };
}
