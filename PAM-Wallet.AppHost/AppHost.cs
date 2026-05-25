var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("WalletDb");

var kafka = builder.AddKafka("kafka", 9092)
    .WithKafkaUI();

var walletApi = builder.AddProject<Projects.Wallet_Api>("wallet-api")
    .WithReference(postgres)
    .WithReference(kafka)
    .WaitFor(postgres)
    .WaitFor(kafka);

builder.Build().Run();
