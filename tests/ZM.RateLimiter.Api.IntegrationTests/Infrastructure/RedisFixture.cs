using Testcontainers.Redis;

namespace ZM.RateLimiter.Api.IntegrationTests.Infrastructure;

public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public static string CreateKeyPrefix(string label) => $"it:{label}:{Guid.NewGuid():N}";

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
