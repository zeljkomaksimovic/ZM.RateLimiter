using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Abstractions;

public interface IRateLimiterAlgorithm
{
    RateLimitingAlgorithmType Type { get; }

    Task<RateLimitResult> ConsumeAsync(RateLimitRequest request, RateLimitPolicy policy, CancellationToken cancellationToken = default);
}
