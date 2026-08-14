namespace ZM.RateLimiter.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "redis";
}
