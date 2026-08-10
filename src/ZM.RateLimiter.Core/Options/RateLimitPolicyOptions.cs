using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Core.Options;

public sealed class RateLimitPolicyOptions
{
    public RateLimitingAlgorithmType Algorithm { get; set; } = RateLimitingAlgorithmType.FixedWindow;

    public long Limit { get; set; }

    public TimeSpan Window { get; set; }
}
