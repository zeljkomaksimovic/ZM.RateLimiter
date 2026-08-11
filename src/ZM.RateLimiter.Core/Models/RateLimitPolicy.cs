using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Core.Models;

public sealed record RateLimitPolicy(string Name, RateLimitingAlgorithmType Algorithm, long Limit, TimeSpan Window);
