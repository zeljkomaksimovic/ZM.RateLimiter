using ZM.RateLimiter.Core.Stores;

namespace ZM.RateLimiter.Core.Abstractions;

public interface ISlidingWindowStore
{
    Task<SlidingWindowStoreResult> ConsumeAsync(
        string key,
        DateTimeOffset timestamp,
        TimeSpan window,
        long limit,
        CancellationToken cancellationToken = default);
}
