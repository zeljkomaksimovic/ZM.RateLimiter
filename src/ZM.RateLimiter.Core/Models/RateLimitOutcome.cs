namespace ZM.RateLimiter.Core.Models;

public sealed record RateLimitOutcome(RateLimitPolicy Policy, RateLimitResult Result);
