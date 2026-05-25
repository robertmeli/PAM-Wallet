using Xunit;

namespace Wallet.IntegrationTests.Fixtures;

[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
