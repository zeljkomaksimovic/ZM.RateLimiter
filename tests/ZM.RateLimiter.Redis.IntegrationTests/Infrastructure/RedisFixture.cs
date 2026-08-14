using StackExchange.Redis;
using Testcontainers.Redis;

namespace ZM.RateLimiter.Redis.IntegrationTests.Infrastructure;

public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    private IConnectionMultiplexer? _connectionMultiplexer;

    public string ConnectionString => _container.GetConnectionString();

    public IConnectionMultiplexer Connection =>
        _connectionMultiplexer ?? throw new InvalidOperationException("The Redis fixture has not been initialised.");

    public static string CreateKeyPrefix(string label) => $"it:{label}:{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _connectionMultiplexer = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _connectionMultiplexer?.Dispose();

        await _container.DisposeAsync();
    }
}
