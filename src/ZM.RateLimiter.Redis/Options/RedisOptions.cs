namespace ZM.RateLimiter.Redis.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    public string ConnectionString { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = "ratelimit";
    public int Database { get; set; } = -1;
}