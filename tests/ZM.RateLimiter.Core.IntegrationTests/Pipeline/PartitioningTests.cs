using FluentAssertions;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

namespace ZM.RateLimiter.Core.IntegrationTests.Pipeline;

public sealed class PartitioningTests
{
    private const string FreeKey = "demo-free-key";
    private const string OtherFreeKey = "another-free-key";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static RateLimiterHarness CreateHarness(RateLimitingAlgorithmType algorithm, long limit) =>
        new RateLimiterHarnessBuilder()
            .WithPolicy("free", algorithm, limit, Window)
            .WithApiKey(FreeKey, "free")
            .WithApiKey(OtherFreeKey, "free")
            .Build();

    [Theory]
    [InlineData(RateLimitingAlgorithmType.FixedWindow)]
    [InlineData(RateLimitingAlgorithmType.SlidingWindow)]
    public async Task Consume_WithDifferentResources_KeepsIndependentCounters(RateLimitingAlgorithmType algorithm)
    {
        // Arrange
        using var harness = CreateHarness(algorithm, limit: 1);

        // Act
        var firstReports = await harness.RateLimiter.ConsumeAsync(FreeKey, "reports");
        var secondReports = await harness.RateLimiter.ConsumeAsync(FreeKey, "reports");
        var firstExports = await harness.RateLimiter.ConsumeAsync(FreeKey, "exports");
        var unscoped = await harness.RateLimiter.ConsumeAsync(FreeKey);

        // Assert
        firstReports!.Result.IsAllowed.Should().BeTrue();
        secondReports!.Result.IsAllowed.Should().BeFalse();
        firstExports!.Result.IsAllowed.Should().BeTrue();
        unscoped!.Result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(RateLimitingAlgorithmType.FixedWindow)]
    [InlineData(RateLimitingAlgorithmType.SlidingWindow)]
    public async Task Consume_WithDifferentApiKeysOnTheSamePolicy_KeepsIndependentCounters(
        RateLimitingAlgorithmType algorithm)
    {
        // Arrange
        using var harness = CreateHarness(algorithm, limit: 1);

        // Act
        var first = await harness.RateLimiter.ConsumeAsync(FreeKey);
        var firstAgain = await harness.RateLimiter.ConsumeAsync(FreeKey);
        var other = await harness.RateLimiter.ConsumeAsync(OtherFreeKey);

        // Assert
        first!.Result.IsAllowed.Should().BeTrue();
        firstAgain!.Result.IsAllowed.Should().BeFalse();
        other!.Result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Consume_NeverPutsTheRawApiKeyIntoAStoreKey()
    {
        // Arrange
        using var harness = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        await harness.RateLimiter.ConsumeAsync(FreeKey);

        // Assert
        // RateLimiterService partitions on a truncated SHA-256 digest, so the key never reaches storage.
        var storeKey = harness.FixedWindowStore.ObservedKeys.Should().ContainSingle().Subject;

        storeKey.Should().NotContain(FreeKey);

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
        await harness.RateLimiter.ConsumeAsync(FreeKey, "reports");

        // Assert
        var storeKey = harness.FixedWindowStore.ObservedKeys.Should().ContainSingle().Subject;
        var segments = storeKey.Split(':');

        segments.Should().HaveCount(4);
        segments[2].Should().Be("reports");
    }

    [Fact]
    public async Task Consume_ProducesAStablePartitionForTheSameApiKey()
    {
        // Arrange
        using var harnessOne = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);
        using var harnessTwo = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        await harnessOne.RateLimiter.ConsumeAsync(FreeKey);
        await harnessTwo.RateLimiter.ConsumeAsync(FreeKey);

        // Assert
        harnessTwo.FixedWindowStore.ObservedKeys
            .Should().BeEquivalentTo(harnessOne.FixedWindowStore.ObservedKeys);
    }

    [Fact]
    public async Task Consume_WithAnUnknownApiKey_ReturnsNullAndTouchesNoStore()
    {
        // Arrange
        using var harness = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        var outcome = await harness.RateLimiter.ConsumeAsync("unknown-key");

        // Assert
        outcome.Should().BeNull();
        harness.FixedWindowStore.ObservedKeys.Should().BeEmpty();
        harness.SlidingWindowStore.ObservedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_WithABlankApiKey_Throws()
    {
        // Arrange
        using var harness = CreateHarness(RateLimitingAlgorithmType.FixedWindow, limit: 5);

        // Act
        var act = async () => await harness.RateLimiter.ConsumeAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
