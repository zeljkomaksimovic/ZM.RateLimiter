namespace ZM.RateLimiter.Api.Contracts
{
    public sealed record ConsumeRateLimitRequest(string? Resource = null);
}
