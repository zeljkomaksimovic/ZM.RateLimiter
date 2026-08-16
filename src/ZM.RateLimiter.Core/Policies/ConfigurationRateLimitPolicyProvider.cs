using Microsoft.Extensions.Options;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Models;
using ZM.RateLimiter.Core.Options;

namespace ZM.RateLimiter.Core.Policies;

public sealed class ConfigurationRateLimitPolicyProvider : IRateLimitPolicyProvider
{
    private readonly IOptionsMonitor<RateLimitingOptions> _options;

    public ConfigurationRateLimitPolicyProvider(IOptionsMonitor<RateLimitingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    public ValueTask<RateLimitPolicy?> GetPolicyAsync(string clientKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);

        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.CurrentValue;

        if (!options.ClientPolicies.TryGetValue(clientKey, out var policyName))
        {
            if (string.IsNullOrWhiteSpace(options.DefaultPolicy))
            {
                return ValueTask.FromResult<RateLimitPolicy?>(null);
            }

            policyName = options.DefaultPolicy;
        }

        if (!options.Policies.TryGetValue(policyName, out var policy))
        {
            return ValueTask.FromResult<RateLimitPolicy?>(null);
        }

        return ValueTask.FromResult<RateLimitPolicy?>(
            new RateLimitPolicy(
                policyName,
                policy.Algorithm,
                policy.Limit,
                policy.Window));
    }
}
