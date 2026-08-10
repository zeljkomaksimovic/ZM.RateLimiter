using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Core.Abstractions
{
    public interface IRateLimitingAlgorithmFactory
    {
        IRateLimiterAlgorithm Resolve(RateLimitingAlgorithmType algorithm);
    }
}
