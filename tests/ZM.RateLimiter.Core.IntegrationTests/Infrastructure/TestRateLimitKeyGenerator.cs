using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

internal sealed class TestRateLimitKeyGenerator : IRateLimitKeyGenerator
{
    private readonly string _keyPrefix;

    public TestRateLimitKeyGenerator(string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        _keyPrefix = keyPrefix;
    }

    public string CreateWindowKey(RateLimitRequest request, DateTimeOffset timestamp, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        var windowStart = timestamp.UtcTicks - (timestamp.UtcTicks % window.Ticks);

        return $"{_keyPrefix}:{request.CompositeKey}:{windowStart}";
    }
}
