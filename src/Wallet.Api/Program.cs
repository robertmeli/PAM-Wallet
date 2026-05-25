using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using Wallet.Api.Services;
using Wallet.Api.Endpoints;
using Wallet.Application.UseCases;
using Wallet.Infrastructure.Orchestration.Orleans;
using Wallet.Infrastructure.Messaging.Kafka;
using Wallet.Infrastructure.Persistence.Postgres;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// --- Infrastructure: Postgres ---
var connectionString = builder.Configuration.GetConnectionString("WalletDb")
    ?? throw new InvalidOperationException("Missing connection string 'WalletDb'");
builder.Services.AddPostgresWalletRepository(connectionString);

// --- Infrastructure: Kafka ---
builder.Services.AddKafkaEventPublisher(builder.Configuration);
builder.Services.AddHostedService<WalletOutboxRelayService>();

// --- Application Use Cases ---
builder.Services.AddScoped<CreateWalletHandler>();
builder.Services.AddScoped<AddFundsHandler>();
builder.Services.AddScoped<DeductFundsHandler>();
builder.Services.AddScoped<GetBalanceHandler>();

// --- Orleans Silo ---
builder.UseOrleans(silo =>
{
    silo.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionString;
    });
    silo.AddAdoNetGrainStorage("Default", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionString;
    });
});

// --- API ---
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

var startupLogger = app.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("StartupMigrations");

const int migrationMaxAttempts = 5;
for (var attempt = 1; attempt <= migrationMaxAttempts; attempt++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<WalletDbContext>()
            .Database.MigrateAsync();
        break;
    }
    catch (Exception ex) when (attempt < migrationMaxAttempts)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        startupLogger.LogWarning(
            ex,
            "Database migration attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s.",
            attempt,
            migrationMaxAttempts,
            delay.TotalSeconds);
        await Task.Delay(delay);
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapWalletEndpoints();

app.Run();

public partial class Program { }
