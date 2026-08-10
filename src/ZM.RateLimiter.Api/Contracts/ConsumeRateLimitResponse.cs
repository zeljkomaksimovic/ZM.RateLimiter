namespace ZM.RateLimiter.Api.Contracts
{
    public sealed record ConsumeRateLimitResponse(
        string Policy,
        bool Allowed,
        long Limit,
        long Remaining,
        TimeSpan? RetryAfter);
}
