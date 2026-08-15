using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Abstractions;

public interface IRateLimitPolicyProvider
{
    ValueTask<RateLimitPolicy?> GetPolicyAsync(string clientKey, CancellationToken cancellationToken = default);
}
