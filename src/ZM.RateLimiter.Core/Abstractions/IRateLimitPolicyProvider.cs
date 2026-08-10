using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Abstractions;

public interface IRateLimitPolicyProvider
{
    ValueTask<RateLimitPolicy?> GetPolicyAsync(string apiKey, CancellationToken cancellationToken = default);
}
