using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Stores;
using ZM.RateLimiter.Redis.Options;

namespace ZM.RateLimiter.Redis.Stores;

public sealed class RedisSlidingWindowStore : ISlidingWindowStore
{
    private const string ConsumeScript =
        """
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local cutoff = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local ttl = tonumber(ARGV[4])
        local member = ARGV[5]

        redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoff)

        local count = redis.call('ZCARD', key)
        local added = 0

        if count < limit then
            redis.call('ZADD', key, now, member)
            count = count + 1
            added = 1
        end

        redis.call('PEXPIRE', key, ttl)

        local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
        local oldestScore = -1

        if oldest[2] then
            oldestScore = tonumber(oldest[2])
        end

        return { added, count, oldestScore }
        """;

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;

    public RedisSlidingWindowStore(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(options);

        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value;
    }

    public async Task<SlidingWindowStoreResult> ConsumeAsync(
        string key,
        DateTimeOffset timestamp,
        TimeSpan window,
        long limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        cancellationToken.ThrowIfCancellationRequested();

        var database = _connectionMultiplexer.GetDatabase(_options.Database);

        var now = timestamp.ToUnixTimeMilliseconds();
        var windowMilliseconds = (long)window.TotalMilliseconds;
        var member = $"{now}-{Guid.NewGuid():N}";

        var result = await database.ScriptEvaluateAsync(
            ConsumeScript,
            [$"{_options.KeyPrefix}:{key}"],
            [now, now - windowMilliseconds, limit, windowMilliseconds, member]);

        var values = (RedisValue[]?)result;

        if (values is not { Length: 3 })
        {
            throw new InvalidOperationException("The sliding window script returned an unexpected reply.");
        }

        var added = (long)values[0] == 1;
        var count = (long)values[1];
        var oldestScore = (long)values[2];

        DateTimeOffset? oldestTimestamp = oldestScore >= 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(oldestScore)
            : null;

        return new SlidingWindowStoreResult(added, count, oldestTimestamp);
    }
}
