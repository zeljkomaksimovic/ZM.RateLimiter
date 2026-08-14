using FluentAssertions;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Core.IntegrationTests.Pipeline;

public sealed class FixedWindowPipelineTests
{
    private const string ApiKey = "fixed-window-key";
    private const long Limit = 3;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Twenty seconds into a window, so retry-after assertions are not trivially the full window.
    private static readonly DateTimeOffset StartTime = RateLimiterHarnessBuilder.DefaultStartTime.AddSeconds(20);

    private static RateLimiterHarness CreateHarness() =>
        new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, Limit, Window)
            .WithApiKey(ApiKey, "free")
            .StartingAt(StartTime)
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
            outcomes.Add((await harness.RateLimiter.ConsumeAsync(ApiKey))!);
        }

        // Assert
        outcomes.Should().AllSatisfy(outcome =>
        {
            outcome.Policy.Name.Should().Be("free");
            outcome.Result.IsAllowed.Should().BeTrue();
            outcome.Result.Limit.Should().Be(Limit);
            outcome.Result.RetryAfter.Should().BeNull();
        });

        outcomes.Select(outcome => outcome.Result.Remaining).Should().Equal(2, 1, 0);
    }

    [Fact]
    public async Task Consume_BeyondTheLimit_DeniesWithTheTimeLeftInTheWindow()
    {
        // Arrange
        using var harness = CreateHarness();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await harness.RateLimiter.ConsumeAsync(ApiKey);
        }

        // Act
        var outcome = await harness.RateLimiter.ConsumeAsync(ApiKey);

        // Assert
        outcome.Should().NotBeNull();
        outcome!.Result.IsAllowed.Should().BeFalse();
        outcome.Result.Limit.Should().Be(Limit);
        outcome.Result.Remaining.Should().Be(0);
        outcome.Result.RetryAfter.Should().Be(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public async Task Consume_AfterTheWindowRolls_AllowsAgainUnderANewKey()
    {
        // Arrange
        using var harness = CreateHarness();

        for (var attempt = 0; attempt < Limit + 1; attempt++)
        {
            await harness.RateLimiter.ConsumeAsync(ApiKey);
        }

        // Act
        harness.TimeProvider.Advance(TimeSpan.FromSeconds(40));

        var outcome = await harness.RateLimiter.ConsumeAsync(ApiKey);

        // Assert
        outcome.Should().NotBeNull();
        outcome!.Result.IsAllowed.Should().BeTrue();
        outcome.Result.Remaining.Should().Be(Limit - 1);

        // The window boundary is part of the key, so a rollover starts a fresh counter.
        harness.FixedWindowStore.ObservedKeys.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Consume_StaysWithinTheSameWindowKeyUntilTheBoundary()
    {
        // Arrange
        using var harness = CreateHarness();

        // Act
        await harness.RateLimiter.ConsumeAsync(ApiKey);
        harness.TimeProvider.Advance(TimeSpan.FromSeconds(39));
        await harness.RateLimiter.ConsumeAsync(ApiKey);

        // Assert
        harness.FixedWindowStore.ObservedKeys.Should().HaveCount(2);
        harness.FixedWindowStore.ObservedKeys.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Consume_ForAFixedWindowPolicy_NeverTouchesTheSlidingWindowStore()
    {
        // Arrange
        using var harness = CreateHarness();

        // Act
        await harness.RateLimiter.ConsumeAsync(ApiKey);

        // Assert
        harness.FixedWindowStore.ObservedKeys.Should().ContainSingle();
        harness.SlidingWindowStore.ObservedKeys.Should().BeEmpty();
    }
}
