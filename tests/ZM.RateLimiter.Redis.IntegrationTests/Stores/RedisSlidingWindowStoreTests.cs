using FluentAssertions;
using Microsoft.Extensions.Options;
using ZM.RateLimiter.Redis.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Redis.Options;
using ZM.RateLimiter.Redis.Stores;

namespace ZM.RateLimiter.Redis.IntegrationTests.Stores;

[Collection(RedisCollection.Name)]
public sealed class RedisSlidingWindowStoreTests
{
    private const long Limit = 3;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly RedisFixture _fixture;
    private readonly string _keyPrefix = RedisFixture.CreateKeyPrefix("sliding");

    public RedisSlidingWindowStoreTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConsumeAsync_UnderTheLimit_AdmitsAndReportsTheOldestEntry()
    {
        // Arrange
        var store = CreateStore();
        var key = nameof(ConsumeAsync_UnderTheLimit_AdmitsAndReportsTheOldestEntry);

        // Act
        var first = await store.ConsumeAsync(key, StartTime, Window, Limit);
        var second = await store.ConsumeAsync(key, StartTime.AddSeconds(10), Window, Limit);

        // Assert
        first.Added.Should().BeTrue();
        first.Count.Should().Be(1);
        first.OldestTimestamp.Should().Be(StartTime);

        second.Added.Should().BeTrue();
        second.Count.Should().Be(2);
        second.OldestTimestamp.Should().Be(StartTime);
    }

    [Fact]
    public async Task ConsumeAsync_AtTheLimit_RejectsWithoutGrowingTheSet()
    {
        // Arrange
        var store = CreateStore();
        var key = nameof(ConsumeAsync_AtTheLimit_RejectsWithoutGrowingTheSet);

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await store.ConsumeAsync(key, StartTime, Window, Limit);
        }

        // Act
        var rejected = await store.ConsumeAsync(key, StartTime.AddSeconds(30), Window, Limit);

        // Assert
        rejected.Added.Should().BeFalse();
        rejected.Count.Should().Be(Limit);
        rejected.OldestTimestamp.Should().Be(StartTime);

        (await SortedSetLengthAsync(key)).Should().Be(Limit);
    }

    [Fact]
    public async Task ConsumeAsync_AtTheSameTimestamp_StillAdmitsDistinctMembers()
    {
        // Arrange
        // Members carry a GUID suffix, so identical timestamps must not collapse into one entry.
        var store = CreateStore();
        var key = nameof(ConsumeAsync_AtTheSameTimestamp_StillAdmitsDistinctMembers);

        // Act
        var counts = new List<long>();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            counts.Add((await store.ConsumeAsync(key, StartTime, Window, Limit)).Count);
        }

        // Assert
        counts.Should().Equal(1, 2, 3);
        (await SortedSetLengthAsync(key)).Should().Be(Limit);
    }

    [Fact]
    public async Task ConsumeAsync_OnceTheOldestEntryLeavesTheWindow_AdmitsAgain()
    {
        // Arrange
        var store = CreateStore();
        var key = nameof(ConsumeAsync_OnceTheOldestEntryLeavesTheWindow_AdmitsAgain);

        await store.ConsumeAsync(key, StartTime, Window, Limit);
        await store.ConsumeAsync(key, StartTime.AddSeconds(10), Window, Limit);
        await store.ConsumeAsync(key, StartTime.AddSeconds(20), Window, Limit);

        // Act
        // Exactly one window later the first entry is on the cutoff and gets evicted; the others stay.
        var result = await store.ConsumeAsync(key, StartTime + Window, Window, Limit);

        // Assert
        result.Added.Should().BeTrue();
        result.Count.Should().Be(Limit);
        result.OldestTimestamp.Should().Be(StartTime.AddSeconds(10));
    }

    [Fact]
    public async Task ConsumeAsync_AppliesTheConfiguredKeyPrefixItself()
    {
        // Arrange
        // Unlike the fixed window path, nothing upstream prefixes this key for the store.
        var store = CreateStore();
        var key = nameof(ConsumeAsync_AppliesTheConfiguredKeyPrefixItself);
        var database = _fixture.Connection.GetDatabase();

        // Act
        await store.ConsumeAsync(key, StartTime, Window, Limit);

        // Assert
        (await database.KeyExistsAsync($"{_keyPrefix}:{key}")).Should().BeTrue();
        (await database.KeyExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task ConsumeAsync_SetsAnExpiryMatchingTheWindow()
    {
        // Arrange
        var store = CreateStore();
        var key = nameof(ConsumeAsync_SetsAnExpiryMatchingTheWindow);

        // Act
        await store.ConsumeAsync(key, StartTime, Window, Limit);

        var timeToLive = await _fixture.Connection
            .GetDatabase()
            .KeyTimeToLiveAsync($"{_keyPrefix}:{key}");

        // Assert
        // Abandoned keys have to fall out on their own; otherwise Redis grows without bound.
        timeToLive.Should().NotBeNull();
        timeToLive!.Value.Should().BeGreaterThan(Window - TimeSpan.FromSeconds(1)).And.BeLessThanOrEqualTo(Window);
    }

    [Fact]
    public async Task ConsumeAsync_WritesToTheConfiguredDatabase()
    {
        // Arrange
        const int database = 4;

        var store = CreateStore(database);
        var key = nameof(ConsumeAsync_WritesToTheConfiguredDatabase);

        // Act
        await store.ConsumeAsync(key, StartTime, Window, Limit);

        // Assert
        (await _fixture.Connection.GetDatabase(database).KeyExistsAsync($"{_keyPrefix}:{key}")).Should().BeTrue();
        (await _fixture.Connection.GetDatabase(0).KeyExistsAsync($"{_keyPrefix}:{key}")).Should().BeFalse();
    }

    [Fact]
    public async Task ConsumeAsync_UnderConcurrency_NeverAdmitsMoreThanTheLimit()
    {
        // Arrange
        const long limit = 10;
        const int callers = 50;

        var store = CreateStore();
        var key = nameof(ConsumeAsync_UnderConcurrency_NeverAdmitsMoreThanTheLimit);

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, callers).Select(_ => store.ConsumeAsync(key, StartTime, Window, limit)));

        // Assert
        // The whole check-then-add sequence runs inside one Lua script, so it is atomic.
        results.Count(result => result.Added).Should().Be((int)limit);
        (await SortedSetLengthAsync(key)).Should().Be(limit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ConsumeAsync_WithABlankKey_Throws(string key)
    {
        // Arrange
        var store = CreateStore();

        // Act
        var act = async () => await store.ConsumeAsync(key, StartTime, Window, Limit);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ConsumeAsync_WithANonPositiveWindow_Throws()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var act = async () => await store.ConsumeAsync("bad-window", StartTime, TimeSpan.Zero, Limit);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ConsumeAsync_WithANonPositiveLimit_Throws()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var act = async () => await store.ConsumeAsync("bad-limit", StartTime, Window, limit: 0);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ConsumeAsync_WithACancelledToken_ThrowsWithoutTouchingRedis()
    {
        // Arrange
        var store = CreateStore();
        var key = nameof(ConsumeAsync_WithACancelledToken_ThrowsWithoutTouchingRedis);

        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        // Act
        var act = async () => await store.ConsumeAsync(key, StartTime, Window, Limit, cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        (await _fixture.Connection.GetDatabase().KeyExistsAsync($"{_keyPrefix}:{key}")).Should().BeFalse();
    }

    private RedisSlidingWindowStore CreateStore(int database = -1) =>
        new(
            _fixture.Connection,
            TestOptions.Create(new RedisOptions
            {
                ConnectionString = _fixture.ConnectionString,
                KeyPrefix = _keyPrefix,
                Database = database
            }));

    private async Task<long> SortedSetLengthAsync(string key, int database = -1) =>
        await _fixture.Connection.GetDatabase(database).SortedSetLengthAsync($"{_keyPrefix}:{key}");
}
