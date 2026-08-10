using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Algorithms;

public sealed class FixedWindowAlgorithm : IRateLimiterAlgorithm
{
    private readonly IFixedWindowStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IRateLimitKeyGenerator _keyGenerator;

    public RateLimitingAlgorithmType Type => RateLimitingAlgorithmType.FixedWindow;

    public FixedWindowAlgorithm(IFixedWindowStore store, IRateLimitKeyGenerator keyGenerator, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(keyGenerator);

        _store = store;
        _keyGenerator = keyGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<RateLimitResult> ConsumeAsync(RateLimitRequest request, RateLimitPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);

        var now = _timeProvider.GetUtcNow();

        var windowKey = _keyGenerator.CreateWindowKey(
            request,
            now,
            policy.Window);

        var retryAfter = CalculateRetryAfter(
            now,
            policy.Window);

        var count = await _store.IncrementAsync(
            windowKey,
            policy.Window,
            cancellationToken);

        return count <= policy.Limit
            ? RateLimitResult.Allowed(
                policy.Limit,
                policy.Limit - count)
            : RateLimitResult.Denied(
                policy.Limit,
                retryAfter);
    }

    private static TimeSpan CalculateRetryAfter(DateTimeOffset now, TimeSpan window)
    {
        return TimeSpan.FromTicks(window.Ticks - (now.UtcTicks % window.Ticks));
    }
}