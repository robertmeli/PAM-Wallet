using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wallet.Application.Ports;

namespace Wallet.Infrastructure.Persistence.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresWalletRepository(
        this IServiceCollection services,
        string connectionString)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = 600,
            MinPoolSize = 50,
            CommandTimeout = 30,
            Timeout = 30
        };

        services.AddDbContextPool<WalletDbContext>(opts =>
            opts.UseNpgsql(csb.ConnectionString, npgsql =>
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromMilliseconds(200),
                    errorCodesToAdd: null)));

        services.AddPooledDbContextFactory<WalletDbContext>(opts =>
            opts.UseNpgsql(csb.ConnectionString, npgsql =>
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromMilliseconds(200),
                    errorCodesToAdd: null)));

        services.AddScoped<IWalletRepository>(sp =>
            new PostgresWalletRepository(
                sp.GetRequiredService<IDbContextFactory<WalletDbContext>>(),
                sp.GetRequiredService<ILogger<PostgresWalletRepository>>()));

        return services;
    }
}
