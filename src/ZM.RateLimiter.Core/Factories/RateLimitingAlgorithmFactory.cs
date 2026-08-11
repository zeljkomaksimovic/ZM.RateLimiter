using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Core.Factories;

public sealed class RateLimitingAlgorithmFactory : IRateLimitingAlgorithmFactory
{
    private readonly IReadOnlyDictionary<RateLimitingAlgorithmType, IRateLimiterAlgorithm> _algorithms;

    public RateLimitingAlgorithmFactory(IEnumerable<IRateLimiterAlgorithm> algorithms)
    {
        ArgumentNullException.ThrowIfNull(algorithms);

        _algorithms = algorithms.ToDictionary(algorithm => algorithm.Type);
    }

    public IRateLimiterAlgorithm Resolve(RateLimitingAlgorithmType algorithm)
    {
        if (!_algorithms.TryGetValue(algorithm, out var implementation))
        {
            throw new InvalidOperationException($"No rate limiting algorithm registered for '{algorithm}'.");
        }

        return implementation;
    }
}