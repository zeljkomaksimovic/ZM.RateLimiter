using FluentAssertions;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

namespace ZM.RateLimiter.Core.IntegrationTests.Pipeline;

public sealed class PartitioningTests
{
    private const string FreeClientKey = "demo-free-client";
    private const string OtherFreeClientKey = "another-free-client";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static RateLimiterHarness CreateHarness(RateLimitingAlgorithmType algorithm, long limit) =>
        new RateLimiterHarnessBuilder()
            .WithPolicy("free", algorithm, limit, Window)
            .WithClientPolicy(FreeClientKey, "free")
            .WithClientPolicy(OtherFreeClientKey, "free")
            .Build();

    [Theory]
    [InlineData(RateLimitingAlgorithmType.FixedWindow)]
    [InlineData(RateLimitingAlgorithmType.SlidingWindow)]
    public async Task Consume_WithDifferentResources_KeepsIndependentCounters(RateLimitingAlgorithmType algorithm)
    {
        // Arrange
        using var harness = CreateHarness(algorithm, limit: 1);

        // Act
        var firstReports = await harness.RateLimiter.ConsumeAsync(FreeClientKey, "reports");
        var secondReports = await harness.RateLimiter.ConsumeAsync(FreeClientKey, "reports");
        var firstExports = await harness.RateLimiter.ConsumeAsync(FreeClientKey, "exports");
        var unscoped = await harness.RateLimiter.ConsumeAsync(FreeClientKey);

        // Assert
        firstReports!.Result.IsAllowed.Should().BeTrue();
        secondReports!.Result.IsAllowed.Should().BeFalse();
        firstExports!.Result.IsAllowed.Should().BeTrue();
        unscoped!.Result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(RateLimitingAlgorithmType.FixedWindow)]
    [InlineData(RateLimitingAlgorithmType.SlidingWindow)]
    public async Task Consume_WithDifferentClientKeysOnTheSamePolicy_KeepsIndependentCounters(
        RateLimitingAlgorithmType algorithm)
    {
        // Arrange
        using var harness = CreateHarness(algorithm, limit: 1);

        // Act
        var first = await harness.RateLimiter.ConsumeAsync(FreeClientKey);
        var firstAgain = await harness.RateLimiter.ConsumeAsync(FreeClientKey);
        var other = await harness.RateLimiter.ConsumeAsync(OtherFreeClientKey);

        // Assert
        first!.Result.IsAllowed.Should().BeTrue();
        firstAgain!.Result.IsAllowed.Should().BeFalse();
        other!.Result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Consume_NeverPutsTheRawClientKeyIntoAStoreKey()
    {
        // Arrange
        using var harness = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        await harness.RateLimiter.ConsumeAsync(FreeClientKey);

        // Assert
        var storeKey = harness.FixedWindowStore.ObservedKeys.Should().ContainSingle().Subject;

        storeKey.Should().NotContain(FreeClientKey);

        var segments = storeKey.Split(':');

        segments.Should().HaveCount(3);
        segments[0].Should().Be("test");
        segments[1].Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Consume_PutsTheResourceIntoTheCompositeKey()
    {
        // Arrange
        using var harness = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        await harness.RateLimiter.ConsumeAsync(FreeClientKey, "reports");

        // Assert
        var storeKey = harness.FixedWindowStore.ObservedKeys.Should().ContainSingle().Subject;
        var segments = storeKey.Split(':');

        segments.Should().HaveCount(4);
        segments[2].Should().Be("reports");
    }

    [Fact]
    public async Task Consume_ProducesAStablePartitionForTheSameClientKey()
    {
        // Arrange
        using var harnessOne = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);
        using var harnessTwo = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        await harnessOne.RateLimiter.ConsumeAsync(FreeClientKey);
        await harnessTwo.RateLimiter.ConsumeAsync(FreeClientKey);

        // Assert
        harnessTwo.FixedWindowStore.ObservedKeys
            .Should().BeEquivalentTo(harnessOne.FixedWindowStore.ObservedKeys);
    }

    [Fact]
    public async Task Consume_WithAnUnknownClientKey_ReturnsNullAndTouchesNoStore()
    {
        // Arrange
        using var harness = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        var outcome = await harness.RateLimiter.ConsumeAsync("unknown-client");

        // Assert
        outcome.Should().BeNull();
        harness.FixedWindowStore.ObservedKeys.Should().BeEmpty();
        harness.SlidingWindowStore.ObservedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_WithAnUnknownClientKey_AndADefaultPolicy_KeepsAPartitionPerClient()
    {
        // Arrange
        using var harness = new RateLimiterHarnessBuilder()
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 1, window: Window)
            .WithDefaultPolicy("free")
            .Build();

        // Act
        var first = await harness.RateLimiter.ConsumeAsync("stranger-one");
        var firstAgain = await harness.RateLimiter.ConsumeAsync("stranger-one");
        var other = await harness.RateLimiter.ConsumeAsync("stranger-two");

        // Assert
        first!.Result.IsAllowed.Should().BeTrue();
        firstAgain!.Result.IsAllowed.Should().BeFalse();
        other!.Result.IsAllowed.Should().BeTrue();

        harness.FixedWindowStore.ObservedKeys.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Consume_WithABlankClientKey_Throws()
    {
        // Arrange
        using var harness = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        var act = async () => await harness.RateLimiter.ConsumeAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
