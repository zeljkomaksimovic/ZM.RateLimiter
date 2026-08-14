using FluentAssertions;
using Microsoft.Extensions.Options;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

namespace ZM.RateLimiter.Core.IntegrationTests.OptionsBinding;

public sealed class RateLimitingOptionsBindingTests
{
    [Fact]
    public void Options_BindPoliciesAndApiKeysFromConfiguration()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 60, window: TimeSpan.FromMinutes(1))
            .WithPolicy("pro", RateLimitingAlgorithmType.SlidingWindow, limit: 1000, window: TimeSpan.FromSeconds(90))
            .WithApiKey("demo-free-key", "free")
            .WithApiKey("demo-pro-key", "pro")
            .WithDefaultPolicy("free")
            .Build();

        // Act
        var options = harness.Options;

        // Assert
        options.DefaultPolicy.Should().Be("free");
        options.Policies.Should().HaveCount(2);
        options.Policies["free"].Algorithm.Should().Be(RateLimitingAlgorithmType.FixedWindow);
        options.Policies["free"].Limit.Should().Be(60);
        options.Policies["free"].Window.Should().Be(TimeSpan.FromMinutes(1));
        options.Policies["pro"].Algorithm.Should().Be(RateLimitingAlgorithmType.SlidingWindow);
        options.Policies["pro"].Window.Should().Be(TimeSpan.FromSeconds(90));
        options.ApiKeys.Should().Contain(new KeyValuePair<string, string>("demo-free-key", "free"));
        options.ApiKeys.Should().Contain(new KeyValuePair<string, string>("demo-pro-key", "pro"));
    }

    [Fact]
    public async Task Consume_ResolvesPolicyNamesCaseInsensitively()
    {
        // Arrange
        // The API key points at "FREE" while the policy is declared as "free".
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithApiKey("demo-key", "FREE")
            .Build();

        // Act
        var outcome = await harness.RateLimiter.ConsumeAsync("demo-key");

        // Assert
        outcome.Should().NotBeNull();
        outcome!.Policy.Limit.Should().Be(5);
        outcome.Policy.Algorithm.Should().Be(RateLimitingAlgorithmType.FixedWindow);
    }

    [Fact]
    public async Task Consume_MatchesApiKeysCaseSensitively()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithApiKey("demo-key", "free")
            .Build();

        // Act
        var exact = await harness.RateLimiter.ConsumeAsync("demo-key");
        var wrongCase = await harness.RateLimiter.ConsumeAsync("DEMO-KEY");

        // Assert
        exact.Should().NotBeNull();
        wrongCase.Should().BeNull();
    }

    [Fact]
    public void Options_WithNoPolicies_FailValidation()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder().Build();

        // Act
        var act = () => harness.Options;

        // Assert
        act.Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainMatch("*must define at least one policy*");
    }

    [Fact]
    public void Options_WithANonPositiveLimitAndWindow_ReportEveryFailure()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("broken", RateLimitingAlgorithmType.FixedWindow, limit: 0, window: TimeSpan.Zero)
            .Build();

        // Act
        var act = () => harness.Options;

        // Assert
        // The validator accumulates rather than short-circuiting, so one run surfaces both problems.
        var failures = act.Should().Throw<OptionsValidationException>().Which.Failures;

        failures.Should().HaveCount(2);
        failures.Should().ContainMatch("*'broken' must have a Limit greater than zero*");
        failures.Should().ContainMatch("*'broken' must have a Window greater than zero*");
    }

    [Fact]
    public void Options_WithAnUnresolvableDefaultPolicy_FailValidation()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithDefaultPolicy("ghost")
            .Build();

        // Act
        var act = () => harness.Options;

        // Assert
        act.Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainMatch("*DefaultPolicy 'ghost' does not match any configured policy*");
    }

    [Fact]
    public void Options_WithAnApiKeyBoundToAMissingPolicy_FailWithoutLeakingTheKey()
    {
        // Arrange
        const string apiKey = "super-secret-key";

        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithApiKey(apiKey, "ghost")
            .Build();

        // Act
        var act = () => harness.Options;

        // Assert
        var failures = act.Should().Throw<OptionsValidationException>().Which.Failures.ToList();

        failures.Should().ContainMatch("*mapped to policy 'ghost', which is not configured*");
        failures.Should().NotContainMatch($"*{apiKey}*");
    }

    [Fact]
    public void Options_WithAValidConfiguration_PassValidation()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithApiKey("demo-key", "free")
            .WithDefaultPolicy("free")
            .Build();

        // Act
        var act = () => harness.Options;

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Consume_WithAnUnmappedApiKey_IgnoresDefaultPolicy()
    {
        // Arrange
        // DefaultPolicy is validated but never consumed by ConfigurationRateLimitPolicyProvider.
        // This pins today's behaviour: unmapped keys are rejected rather than falling back.
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithDefaultPolicy("free")
            .Build();

        // Act
        var outcome = await harness.RateLimiter.ConsumeAsync("never-configured-key");

        // Assert
        outcome.Should().BeNull();
    }
}
