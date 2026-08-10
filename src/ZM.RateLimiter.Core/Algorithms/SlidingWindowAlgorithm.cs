using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Algorithms;

public sealed class SlidingWindowAlgorithm : IRateLimiterAlgorithm
{
    private readonly ISlidingWindowStore _store;
    private readonly TimeProvider _timeProvider;

    public RateLimitingAlgorithmType Type => RateLimitingAlgorithmType.SlidingWindow;

    public SlidingWindowAlgorithm(ISlidingWindowStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<RateLimitResult> ConsumeAsync(RateLimitRequest request, RateLimitPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);

        var now = _timeProvider.GetUtcNow();

        var snapshot = await _store.ConsumeAsync(
            request.CompositeKey,
            now,
            policy.Window,
            policy.Limit,
            cancellationToken);

        if (snapshot.Added)
        {
            return RateLimitResult.Allowed(
                policy.Limit,
                Math.Max(0, policy.Limit - snapshot.Count));
        }

        var retryAfter = CalculateRetryAfter(
            snapshot.OldestTimestamp,
            now,
            policy.Window);

        return RateLimitResult.Denied(
            policy.Limit,
            retryAfter);
    }

    private static TimeSpan CalculateRetryAfter(DateTimeOffset? oldestTimestamp, DateTimeOffset now, TimeSpan window)
    {
        if (oldestTimestamp is null)
        {
            return window;
        }

        var retryAfter = oldestTimestamp.Value + window - now;

        return retryAfter > TimeSpan.Zero
            ? retryAfter
            : TimeSpan.Zero;
    }
}