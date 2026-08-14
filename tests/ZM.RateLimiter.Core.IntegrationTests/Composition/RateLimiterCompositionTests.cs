using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Algorithms;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Policies;
using ZM.RateLimiter.Core.Services;

namespace ZM.RateLimiter.Core.IntegrationTests.Composition;

public sealed class RateLimiterCompositionTests
{
    [Fact]
    public void AddRateLimiterCore_ResolvesTheDocumentedImplementations()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .Build();

        // Act
        var rateLimiter = harness.Services.GetRequiredService<IRateLimiter>();
        var policyProvider = harness.Services.GetRequiredService<IRateLimitPolicyProvider>();

        // Assert
        rateLimiter.Should().BeOfType<RateLimiterService>();
        policyProvider.Should().BeOfType<ConfigurationRateLimitPolicyProvider>();
    }

    [Fact]
    public void AddRateLimiterCore_RegistersTheRateLimiterAsASingleton()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .Build();

        // Act
        var first = harness.Services.GetRequiredService<IRateLimiter>();
        var second = harness.Services.GetRequiredService<IRateLimiter>();

        // Assert
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void AddRateLimiterCore_RegistersBothAlgorithmsBehindTheFactory()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .Build();

        var factory = harness.Services.GetRequiredService<IRateLimitingAlgorithmFactory>();

        // Act
        var fixedWindow = factory.Resolve(RateLimitingAlgorithmType.FixedWindow);
        var slidingWindow = factory.Resolve(RateLimitingAlgorithmType.SlidingWindow);

        // Assert
        fixedWindow.Should().BeOfType<FixedWindowAlgorithm>();
        slidingWindow.Should().BeOfType<SlidingWindowAlgorithm>();
    }

    [Fact]
    public void ResolveAlgorithm_ForAnUnregisteredAlgorithm_Throws()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .Build();

        var factory = harness.Services.GetRequiredService<IRateLimitingAlgorithmFactory>();

        // Act
        var act = () => factory.Resolve((RateLimitingAlgorithmType)999);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*No rate limiting algorithm registered*");
    }

    [Fact]
    public void AddRateLimiterCore_DoesNotReplaceAnAlreadyRegisteredTimeProvider()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .Build();

        // Act
        var timeProvider = harness.Services.GetRequiredService<TimeProvider>();

        // Assert
        timeProvider.Should().BeSameAs(harness.TimeProvider);
        timeProvider.GetUtcNow().Should().Be(RateLimiterHarnessBuilder.DefaultStartTime);
    }

    [Fact]
    public void AddRateLimiterCore_OnItsOwn_CannotSatisfyTheAlgorithmDependencies()
    {
        // Arrange
        // Core registers no IFixedWindowStore, ISlidingWindowStore or IRateLimitKeyGenerator; those
        // come from a storage package such as ZM.RateLimiter.Redis. This pins that contract.
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithoutStores()
            .Build();

        // Act
        var act = () => harness.Services.GetRequiredService<IRateLimiter>();

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(IFixedWindowStore)}*");
    }
}
