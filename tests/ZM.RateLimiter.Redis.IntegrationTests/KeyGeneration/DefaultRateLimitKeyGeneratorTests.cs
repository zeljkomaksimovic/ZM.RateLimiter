using FluentAssertions;
using Microsoft.Extensions.Options;
using ZM.RateLimiter.Core.Models;
using ZM.RateLimiter.Redis.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Redis.KeyGeneration;
using ZM.RateLimiter.Redis.Options;
using ZM.RateLimiter.Redis.Stores;

namespace ZM.RateLimiter.Redis.IntegrationTests.KeyGeneration;

[Collection(RedisCollection.Name)]
public sealed class DefaultRateLimitKeyGeneratorTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly DateTimeOffset WindowStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly RedisFixture _fixture;
    private readonly string _keyPrefix = RedisFixture.CreateKeyPrefix("keys");

    public DefaultRateLimitKeyGeneratorTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateWindowKey_ForTimestampsInsideOneWindow_ReturnsTheSameKey()
    {
        // Arrange
        var generator = CreateGenerator();
        var request = new RateLimitRequest("partition");

        // Act
        var atStart = generator.CreateWindowKey(request, WindowStart, Window);
        var nearTheEnd = generator.CreateWindowKey(request, WindowStart.AddSeconds(59), Window);

        // Assert
        nearTheEnd.Should().Be(atStart);
    }

    [Fact]
    public void CreateWindowKey_AcrossAWindowBoundary_ReturnsDifferentKeys()
    {
        // Arrange
        var generator = CreateGenerator();
        var request = new RateLimitRequest("partition");

        // Act
        var before = generator.CreateWindowKey(request, WindowStart.AddSeconds(59), Window);
        var after = generator.CreateWindowKey(request, WindowStart.AddSeconds(60), Window);

        // Assert
        after.Should().NotBe(before);
    }

    [Fact]
    public void CreateWindowKey_CombinesThePrefixCompositeKeyAndWindowStart()
    {
        // Arrange
        var generator = CreateGenerator();
        var request = new RateLimitRequest("partition");

        // Act
        var key = generator.CreateWindowKey(request, WindowStart.AddSeconds(20), Window);

        // Assert
        key.Should().Be($"{_keyPrefix}:partition:{WindowStart.UtcTicks}");
    }

    [Fact]
    public void CreateWindowKey_IncludesTheResourceWhenPresent()
    {
        // Arrange
        var generator = CreateGenerator();

        // Act
        var key = generator.CreateWindowKey(new RateLimitRequest("partition", "reports"), WindowStart, Window);

        // Assert
        key.Should().Be($"{_keyPrefix}:partition:reports:{WindowStart.UtcTicks}");
    }

    [Fact]
    public void CreateWindowKey_WithANonPositiveWindow_Throws()
    {
        // Arrange
        var generator = CreateGenerator();

        // Act
        var act = () => generator.CreateWindowKey(new RateLimitRequest("partition"), WindowStart, TimeSpan.Zero);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GeneratedKeys_GiveTheFixedWindowStoreOneCounterPerWindow()
    {
        // Arrange
        var generator = CreateGenerator();
        var store = new RedisFixedWindowStore(_fixture.Connection, TestOptions.Create(CreateOptions()));
        var request = new RateLimitRequest($"roundtrip-{Guid.NewGuid():N}");

        // Act
        var firstWindowStart = await store.IncrementAsync(
            generator.CreateWindowKey(request, WindowStart.AddSeconds(20), Window), Window);

        var firstWindowLater = await store.IncrementAsync(
            generator.CreateWindowKey(request, WindowStart.AddSeconds(50), Window), Window);

        var secondWindow = await store.IncrementAsync(
            generator.CreateWindowKey(request, WindowStart.AddSeconds(70), Window), Window);

        // Assert
        firstWindowStart.Should().Be(1);
        firstWindowLater.Should().Be(2);
        secondWindow.Should().Be(1);
    }

    private DefaultRateLimitKeyGenerator CreateGenerator() => new(TestOptions.Create(CreateOptions()));

    private RedisOptions CreateOptions() => new()
    {
        ConnectionString = _fixture.ConnectionString,
        KeyPrefix = _keyPrefix
    };
}
