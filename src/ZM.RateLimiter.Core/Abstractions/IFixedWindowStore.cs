namespace ZM.RateLimiter.Core.Abstractions;

public interface IFixedWindowStore
{
    Task<long> IncrementAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default);
}
