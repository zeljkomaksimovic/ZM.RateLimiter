using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Abstractions
{
    public interface IRateLimitKeyGenerator
    {
        string CreateWindowKey(RateLimitRequest request, DateTimeOffset timestamp, TimeSpan window);
    }
}
