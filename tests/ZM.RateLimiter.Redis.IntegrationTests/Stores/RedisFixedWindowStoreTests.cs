using FluentAssertions;
using Microsoft.Extensions.Options;
using ZM.RateLimiter.Redis.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Redis.Options;
using ZM.RateLimiter.Redis.Stores;

namespace ZM.RateLimiter.Redis.IntegrationTests.Stores;

[Collection(RedisCollection.Name)]
public sealed class RedisFixedWindowStoreTests
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private readonly RedisFixture _fixture;
    private readonly string _keyPrefix = RedisFixture.CreateKeyPrefix("fixed");

    public RedisFixedWindowStoreTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IncrementAsync_ReturnsAMonotonicCountForTheSameKey()
    {
        // Arrange
        var store = CreateStore();
        var key = Key(nameof(IncrementAsync_ReturnsAMonotonicCountForTheSameKey));

        // Act
        var counts = new List<long>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            counts.Add(await store.IncrementAsync(key, TimeToLive));
        }

        // Assert
        counts.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task IncrementAsync_KeepsDistinctKeysIndependent()
    {
        // Arrange
        var store = CreateStore();
        var first = Key("independent-a");
        var second = Key("independent-b");

        // Act
        await store.IncrementAsync(first, TimeToLive);
        await store.IncrementAsync(first, TimeToLive);
        var secondCount = await store.IncrementAsync(second, TimeToLive);

        // Assert
        secondCount.Should().Be(1);
    }

    [Fact]
    public async Task IncrementAsync_SetsTheExpiryOnTheFirstCallAndNeverExtendsIt()
    {
        // Arrange
        var store = CreateStore();
        var key = Key(nameof(IncrementAsync_SetsTheExpiryOnTheFirstCallAndNeverExtendsIt));
        var database = _fixture.Connection.GetDatabase();

        // Act
        await store.IncrementAsync(key, TimeToLive);
        var afterFirst = await database.KeyTimeToLiveAsync(key);

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        await store.IncrementAsync(key, TimeToLive);
        var afterSecond = await database.KeyTimeToLiveAsync(key);

        // Assert
        afterFirst.Should().NotBeNull().And.BeLessThanOrEqualTo(TimeToLive);
        afterSecond.Should().NotBeNull();
        afterSecond!.Value.Should().BeLessThan(afterFirst!.Value);
    }

    [Fact]
    public async Task IncrementAsync_UsesTheSuppliedKeyVerbatim()
    {
        // Arrange
        var store = CreateStore();
        var key = Key(nameof(IncrementAsync_UsesTheSuppliedKeyVerbatim));
        var database = _fixture.Connection.GetDatabase();

        // Act
        await store.IncrementAsync(key, TimeToLive);

        // Assert
        (await database.KeyExistsAsync(key)).Should().BeTrue();
        (await database.KeyExistsAsync($"{_keyPrefix}:{key}")).Should().BeFalse();
    }

    [Fact]
    public async Task IncrementAsync_WritesToTheConfiguredDatabase()
    {
        // Arrange
        const int database = 3;

        var store = CreateStore(database);
        var key = Key(nameof(IncrementAsync_WritesToTheConfiguredDatabase));

        // Act
        await store.IncrementAsync(key, TimeToLive);

        // Assert
        (await _fixture.Connection.GetDatabase(database).KeyExistsAsync(key)).Should().BeTrue();
        (await _fixture.Connection.GetDatabase(0).KeyExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task IncrementAsync_UnderConcurrency_LosesNoIncrements()
    {
        // Arrange
        const int callers = 50;

        var store = CreateStore();
        var key = Key(nameof(IncrementAsync_UnderConcurrency_LosesNoIncrements));

        // Act
        var counts = await Task.WhenAll(
            Enumerable.Range(0, callers).Select(_ => store.IncrementAsync(key, TimeToLive)));

        // Assert
        counts.Should().BeEquivalentTo(Enumerable.Range(1, callers).Select(value => (long)value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IncrementAsync_WithABlankKey_Throws(string key)
    {
        // Arrange
        var store = CreateStore();

        // Act
        var act = async () => await store.IncrementAsync(key, TimeToLive);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IncrementAsync_WithANonPositiveTimeToLive_Throws()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var act = async () => await store.IncrementAsync(Key("bad-ttl"), TimeSpan.Zero);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task IncrementAsync_WithACancelledToken_ThrowsWithoutTouchingRedis()
    {
        // Arrange
        var store = CreateStore();
        var key = Key(nameof(IncrementAsync_WithACancelledToken_ThrowsWithoutTouchingRedis));

        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        // Act
        var act = async () => await store.IncrementAsync(key, TimeToLive, cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        (await _fixture.Connection.GetDatabase().KeyExistsAsync(key)).Should().BeFalse();
    }

    private RedisFixedWindowStore CreateStore(int database = -1) =>
        new(
            _fixture.Connection,
            TestOptions.Create(new RedisOptions
            {
                ConnectionString = _fixture.ConnectionString,
                KeyPrefix = _keyPrefix,
                Database = database
            }));

    private string Key(string name) => $"{_keyPrefix}:{name}";
}
