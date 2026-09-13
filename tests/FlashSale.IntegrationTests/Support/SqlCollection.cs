namespace FlashSale.IntegrationTests.Support;

[CollectionDefinition("SQL", DisableParallelization = true)]
public sealed class SqlCollection : ICollectionFixture<SqlFixture>;
