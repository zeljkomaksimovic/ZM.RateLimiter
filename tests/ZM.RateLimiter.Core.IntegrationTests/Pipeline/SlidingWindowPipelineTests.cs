using FluentAssertions;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.IntegrationTests.Pipeline;

public sealed class SlidingWindowPipelineTests
{
    private const string ClientKey = "sliding-window-client";
    private const long Limit = 3;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static RateLimiterHarness CreateHarness() =>
        new RateLimiterHarnessBuilder()
            .WithPolicy("pro", RateLimitingAlgorithmType.SlidingWindow, Limit, Window)
            .WithClientPolicy(ClientKey, "pro")
            .Build();

    [Fact]
    public async Task Consume_UpToTheLimit_AllowsAndCountsRemainingDown()
    {
        // Arrange
        using var harness = CreateHarness();

        // Act
        var outcomes = new List<RateLimitOutcome>();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            outcomes.Add((await harness.RateLimiter.ConsumeAsync(ClientKey))!);
        }

        // Assert
        outcomes.Should().AllSatisfy(outcome =>
        {
            outcome.Policy.Name.Should().Be("pro");
            outcome.Result.IsAllowed.Should().BeTrue();
            outcome.Result.Limit.Should().Be(Limit);
            outcome.Result.RetryAfter.Should().BeNull();
        });

        outcomes.Select(outcome => outcome.Result.Remaining).Should().Equal(2, 1, 0);
    }

    [Fact]
    public async Task Consume_BeyondTheLimit_DeniesUntilTheOldestEntryLeavesTheWindow()
    {
        // Arrange
        using var harness = CreateHarness();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await harness.RateLimiter.ConsumeAsync(ClientKey);
        }

        // Act
        var immediately = await harness.RateLimiter.ConsumeAsync(ClientKey);

        harness.TimeProvider.Advance(TimeSpan.FromSeconds(25));

        var later = await harness.RateLimiter.ConsumeAsync(ClientKey);

        // Assert
        // Retry-after tracks the oldest entry, so it shrinks as the window slides.
        immediately!.Result.IsAllowed.Should().BeFalse();
        immediately.Result.Remaining.Should().Be(0);
        immediately.Result.RetryAfter.Should().Be(Window);

        later!.Result.IsAllowed.Should().BeFalse();
        later.Result.RetryAfter.Should().Be(TimeSpan.FromSeconds(35));
    }

    [Fact]
    public async Task Consume_OnceTheOldestEntryExpires_AllowsAgain()
    {
        // Arrange
        using var harness = CreateHarness();

        for (var attempt = 0; attempt < Limit + 1; attempt++)
        {
            await harness.RateLimiter.ConsumeAsync(ClientKey);
        }

        // Act
        harness.TimeProvider.Advance(Window);

        var outcome = await harness.RateLimiter.ConsumeAsync(ClientKey);

        // Assert
        outcome.Should().NotBeNull();
        outcome!.Result.IsAllowed.Should().BeTrue();
        outcome.Result.Remaining.Should().Be(Limit - 1);
    }

    [Fact]
    public async Task Consume_DeniedAttempts_DoNotConsumeCapacity()
    {
        // Arrange
        using var harness = CreateHarness();

        for (var attempt = 0; attempt < Limit + 5; attempt++)
        {
            await harness.RateLimiter.ConsumeAsync(ClientKey);
        }

        // Act
        // Half the window later the three admitted entries are still inside it.
        harness.TimeProvider.Advance(TimeSpan.FromSeconds(30));

        var outcome = await harness.RateLimiter.ConsumeAsync(ClientKey);

        // Assert
        outcome!.Result.IsAllowed.Should().BeFalse();
        outcome.Result.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Consume_ForASlidingWindowPolicy_NeverTouchesTheFixedWindowStore()
    {
        // Arrange
        using var harness = CreateHarness();

        // Act
        await harness.RateLimiter.ConsumeAsync(ClientKey);

        // Assert
        harness.SlidingWindowStore.ObservedKeys.Should().ContainSingle();
        harness.FixedWindowStore.ObservedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_ForASlidingWindowPolicy_DoesNotUseTheKeyGenerator()
    {
        // Arrange
        // SlidingWindowAlgorithm passes the composite key straight through; the storage package is
        // responsible for prefixing it. The key must therefore carry no generator prefix.
        using var harness = CreateHarness();

        // Act
        await harness.RateLimiter.ConsumeAsync(ClientKey, "reports");

        // Assert
        harness.SlidingWindowStore.ObservedKeys.Should().ContainSingle()
            .Which.Should().EndWith(":reports").And.NotStartWith("test:");
    }
}
