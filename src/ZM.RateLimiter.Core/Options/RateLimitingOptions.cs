namespace ZM.RateLimiter.Core.Options;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public string? DefaultPolicy { get; set; }

    public Dictionary<string, RateLimitPolicyOptions> Policies { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> ClientPolicies { get; set; } =
        new(StringComparer.Ordinal);
}
