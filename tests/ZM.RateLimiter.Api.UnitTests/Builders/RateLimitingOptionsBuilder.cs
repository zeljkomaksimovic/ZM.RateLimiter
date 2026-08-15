using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Options;

namespace ZM.RateLimiter.Api.UnitTests.Builders
{
    internal static class RateLimitingOptionsBuilder
    {
        public static RateLimitingOptions Build(
            string? defaultPolicy = null,
            IDictionary<string, RateLimitPolicyOptions>? policies = null,
            IDictionary<string, string>? clientPolicies = null)
        {
            var options = new RateLimitingOptions
            {
                DefaultPolicy = defaultPolicy
            };

            policies ??= new Dictionary<string, RateLimitPolicyOptions>
            {
                ["free"] = BuildPolicy()
            };

            clientPolicies ??= new Dictionary<string, string>
            {
                ["demo-free-client"] = "free"
            };

            foreach (var (name, policy) in policies)
            {
                options.Policies[name] = policy;
            }

            foreach (var (clientKey, policyName) in clientPolicies)
            {
                options.ClientPolicies[clientKey] = policyName;
            }

            return options;
        }

        public static RateLimitPolicyOptions BuildPolicy(
            RateLimitingAlgorithmType algorithm = RateLimitingAlgorithmType.FixedWindow,
            long limit = 10,
            TimeSpan? window = null)
        {
            return new RateLimitPolicyOptions
            {
                Algorithm = algorithm,
                Limit = limit,
                Window = window ?? TimeSpan.FromMinutes(1)
            };
        }
    }
}
