using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Api.UnitTests.Builders
{
    internal static class RateLimitPolicyBuilder
    {
        public static RateLimitPolicy Build(
            string name = "free",
            RateLimitingAlgorithmType algorithm = RateLimitingAlgorithmType.FixedWindow,
            long limit = 10,
            TimeSpan? window = null)
        {
            return new RateLimitPolicy(
                name,
                algorithm,
                limit,
                window ?? TimeSpan.FromMinutes(1));
        }
    }
}
