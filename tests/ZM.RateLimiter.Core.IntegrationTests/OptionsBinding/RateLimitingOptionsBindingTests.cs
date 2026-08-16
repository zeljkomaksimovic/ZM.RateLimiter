using FluentAssertions;
using Microsoft.Extensions.Options;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

namespace ZM.RateLimiter.Core.IntegrationTests.OptionsBinding;

public sealed class RateLimitingOptionsBindingTests
{
    [Fact]
    public void Options_BindPoliciesAndClientPoliciesFromConfiguration()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 60, window: TimeSpan.FromMinutes(1))
            .WithPolicy("pro", RateLimitingAlgorithmType.SlidingWindow, limit: 1000, window: TimeSpan.FromSeconds(90))
            .WithClientPolicy("demo-free-client", "free")
            .WithClientPolicy("demo-pro-client", "pro")
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
        options.ClientPolicies.Should().Contain(new KeyValuePair<string, string>("demo-free-client", "free"));
        options.ClientPolicies.Should().Contain(new KeyValuePair<string, string>("demo-pro-client", "pro"));
    }

    [Fact]
    public async Task Consume_ResolvesPolicyNamesCaseInsensitively()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithClientPolicy("demo-client", "FREE")
            .Build();

        // Act
        var outcome = await harness.RateLimiter.ConsumeAsync("demo-client");

        // Assert
        outcome.Should().NotBeNull();
        outcome!.Policy.Limit.Should().Be(5);
        outcome.Policy.Algorithm.Should().Be(RateLimitingAlgorithmType.FixedWindow);
    }

    [Fact]
    public async Task Consume_MatchesClientKeysCaseSensitively()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithClientPolicy("demo-client", "free")
            .Build();

        // Act
        var exact = await harness.RateLimiter.ConsumeAsync("demo-client");
        var wrongCase = await harness.RateLimiter.ConsumeAsync("DEMO-CLIENT");

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
    public void Options_WithAClientKeyBoundToAMissingPolicy_FailWithoutLeakingTheKey()
    {
        // Arrange
        const string clientKey = "super-secret-client-key";

        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithClientPolicy(clientKey, "ghost")
            .Build();

        // Act
        var act = () => harness.Options;

        // Assert
        var failures = act.Should().Throw<OptionsValidationException>().Which.Failures.ToList();

        failures.Should().ContainMatch("*mapped to policy 'ghost', which is not configured*");
        failures.Should().NotContainMatch($"*{clientKey}*");
    }

    [Fact]
    public void Options_WithAValidConfiguration_PassValidation()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithClientPolicy("demo-client", "free")
            .WithDefaultPolicy("free")
            .Build();

        // Act
        var act = () => harness.Options;

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Consume_WithAnUnmappedClientKey_FallsBackToTheDefaultPolicy()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithDefaultPolicy("free")
            .Build();

        // Act
        var outcome = await harness.RateLimiter.ConsumeAsync("never-configured-client");

        // Assert
        outcome.Should().NotBeNull();
        outcome!.Policy.Name.Should().Be("free");
        outcome.Policy.Limit.Should().Be(5);
        outcome.Result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Consume_WithAnUnmappedClientKey_AndNoDefaultPolicy_IsRejected()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .Build();

        // Act
        var outcome = await harness.RateLimiter.ConsumeAsync("never-configured-client");

        // Assert
        outcome.Should().BeNull();
    }
}
