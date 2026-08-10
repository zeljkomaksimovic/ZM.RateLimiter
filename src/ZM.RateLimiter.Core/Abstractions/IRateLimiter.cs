using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Abstractions;

public interface IRateLimiter
{
    Task<RateLimitOutcome?> ConsumeAsync(string apiKey, string? resource = null, CancellationToken cancellationToken = default);
}
