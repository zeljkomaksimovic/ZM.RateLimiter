using Microsoft.Extensions.Options;

namespace ZM.RateLimiter.Core.Options;

public sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        var failures = new List<string>();

        if (options.Policies.Count == 0)
        {
            failures.Add($"'{RateLimitingOptions.SectionName}:Policies' must define at least one policy.");
        }

        foreach (var (policyName, policy) in options.Policies)
        {
            if (string.IsNullOrWhiteSpace(policyName))
            {
                failures.Add("A policy name must not be empty.");
                continue;
            }

            if (policy.Limit <= 0)
            {
                failures.Add($"Policy '{policyName}' must have a Limit greater than zero.");
            }

            if (policy.Window <= TimeSpan.Zero)
            {
                failures.Add($"Policy '{policyName}' must have a Window greater than zero.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultPolicy) &&
            !options.Policies.ContainsKey(options.DefaultPolicy))
        {
            failures.Add(
                $"DefaultPolicy '{options.DefaultPolicy}' does not match any configured policy.");
        }

        foreach (var (clientKey, policyName) in options.ClientPolicies)
        {
            if (string.IsNullOrWhiteSpace(clientKey))
            {
                failures.Add("A client key must not be empty.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(policyName) || !options.Policies.ContainsKey(policyName))
            {
                failures.Add(
                    $"A client key is mapped to policy '{policyName}', which is not configured.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
