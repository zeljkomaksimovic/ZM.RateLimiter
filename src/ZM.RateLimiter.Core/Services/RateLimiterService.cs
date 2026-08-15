using System.Security.Cryptography;
using System.Text;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.Services;

public sealed class RateLimiterService : IRateLimiter
{
    private readonly IRateLimitPolicyProvider _policyProvider;
    private readonly IRateLimitingAlgorithmFactory _algorithmFactory;

    public RateLimiterService(IRateLimitPolicyProvider policyProvider, IRateLimitingAlgorithmFactory algorithmFactory)
    {
        ArgumentNullException.ThrowIfNull(policyProvider);
        ArgumentNullException.ThrowIfNull(algorithmFactory);

        _policyProvider = policyProvider;
        _algorithmFactory = algorithmFactory;
    }

    public async Task<RateLimitOutcome?> ConsumeAsync(string clientKey, string? resource = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);

        var policy = await _policyProvider.GetPolicyAsync(clientKey, cancellationToken);

        if (policy is null)
        {
            return null;
        }

        var algorithm = _algorithmFactory.Resolve(policy.Algorithm);

        var result = await algorithm.ConsumeAsync(
            new RateLimitRequest(CreatePartitionKey(clientKey), resource),
            policy,
            cancellationToken);

        return new RateLimitOutcome(policy, result);
    }

    private static string CreatePartitionKey(string clientKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(clientKey));

        return Convert.ToHexStringLower(digest.AsSpan(0, 16));
    }
}
