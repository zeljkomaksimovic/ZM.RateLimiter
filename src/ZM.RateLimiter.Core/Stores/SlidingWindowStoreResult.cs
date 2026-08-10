namespace ZM.RateLimiter.Core.Stores;

public readonly record struct SlidingWindowStoreResult(bool Added, long Count, DateTimeOffset? OldestTimestamp);
