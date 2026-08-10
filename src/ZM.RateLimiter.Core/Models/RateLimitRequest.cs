namespace ZM.RateLimiter.Core.Models;

public sealed record RateLimitRequest(string Key, string? Resource = null)
{
    public string CompositeKey => string.IsNullOrWhiteSpace(Resource) ? Key : $"{Key}:{Resource}";
}
