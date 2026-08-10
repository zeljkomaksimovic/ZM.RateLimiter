using System.Security.Cryptography;
using System.Text;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Services;

public sealed class RateLimiterService : IRateLimiter
{
    private readonly IRateLimitPolicyProvider _policyProvider;
    private readonly IRateLimitingAlgorithmFactory _algorithmFactory;

    public RateLimiterService(
        IRateLimitPolicyProvider policyProvider,
        IRateLimitingAlgorithmFactory algorithmFactory)
    {
        ArgumentNullException.ThrowIfNull(policyProvider);
        ArgumentNullException.ThrowIfNull(algorithmFactory);

        _policyProvider = policyProvider;
        _algorithmFactory = algorithmFactory;
    }

    public async Task<RateLimitOutcome?> ConsumeAsync(string apiKey, string? resource = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var policy = await _policyProvider.GetPolicyAsync(apiKey, cancellationToken);

        if (policy is null)
        {
            return null;
        }

        var algorithm = _algorithmFactory.Resolve(policy.Algorithm);

        var result = await algorithm.ConsumeAsync(
            new RateLimitRequest(
                CreatePartitionKey(apiKey),
                resource),
            policy,
            cancellationToken);

        return new RateLimitOutcome(policy, result);
    }

    private static string CreatePartitionKey(string apiKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

        return Convert.ToHexStringLower(digest.AsSpan(0, 16));
    }
}
