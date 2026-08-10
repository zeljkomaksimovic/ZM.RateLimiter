using Microsoft.Extensions.Options;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Models;
using ZM.RateLimiter.Redis.Options;

namespace ZM.RateLimiter.Redis.KeyGeneration;

public sealed class DefaultRateLimitKeyGenerator : IRateLimitKeyGenerator
{
    private readonly RedisOptions _options;

    public DefaultRateLimitKeyGenerator(IOptions<RedisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    public string CreateWindowKey(RateLimitRequest request, DateTimeOffset timestamp, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        var windowStart = timestamp.UtcTicks - (timestamp.UtcTicks % window.Ticks);

        return $"{_options.KeyPrefix}:{request.CompositeKey}:{windowStart}";
    }
}
