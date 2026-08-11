using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Redis.Options;

namespace ZM.RateLimiter.Redis.Stores;

public sealed class RedisFixedWindowStore : IFixedWindowStore
{
    private const string IncrementScript =
        """
        local count = redis.call('INCR', KEYS[1])

        if count == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end

        return count
        """;

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;

    public RedisFixedWindowStore(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(options);

        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value; 
    }

    public async Task<long> IncrementAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeToLive, TimeSpan.Zero);

        cancellationToken.ThrowIfCancellationRequested();

        var database = _connectionMultiplexer.GetDatabase(_options.Database);

        var result = await database.ScriptEvaluateAsync(
            IncrementScript,
            [key],
            [(long)timeToLive.TotalMilliseconds]);

        return (long)result;
    }
}
