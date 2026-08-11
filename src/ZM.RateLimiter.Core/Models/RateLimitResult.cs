namespace ZM.RateLimiter.Core.Models;

public sealed record RateLimitResult(bool IsAllowed, long Limit, long Remaining, TimeSpan? RetryAfter)
{
    public static RateLimitResult Allowed(long limit, long remaining) =>
        new RateLimitResult(true, limit, remaining, RetryAfter: null);

    public static RateLimitResult Denied(long limit, TimeSpan retryAfter) =>
        new RateLimitResult(false, limit, Remaining: 0, retryAfter);
}
