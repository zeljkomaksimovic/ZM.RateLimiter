using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Redis.DependencyInjection;
using ZM.RateLimiter.Redis.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Redis.KeyGeneration;
using ZM.RateLimiter.Redis.Options;
using ZM.RateLimiter.Redis.Stores;

namespace ZM.RateLimiter.Redis.IntegrationTests.Composition;

[Collection(RedisCollection.Name)]
public sealed class RedisRateLimiterRegistrationTests
{
    private readonly RedisFixture _fixture;
    private readonly string _keyPrefix = RedisFixture.CreateKeyPrefix("registration");

    public RedisRateLimiterRegistrationTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void AddRedisRateLimiter_ResolvesTheDocumentedImplementations()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var keyGenerator = provider.GetRequiredService<IRateLimitKeyGenerator>();
        var fixedWindowStore = provider.GetRequiredService<IFixedWindowStore>();
        var slidingWindowStore = provider.GetRequiredService<ISlidingWindowStore>();

        // Assert
        keyGenerator.Should().BeOfType<DefaultRateLimitKeyGenerator>();
        fixedWindowStore.Should().BeOfType<RedisFixedWindowStore>();
        slidingWindowStore.Should().BeOfType<RedisSlidingWindowStore>();
    }

    [Fact]
    public void AddRedisRateLimiter_RegistersEverythingAsSingletons()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act & Assert
        provider.GetRequiredService<IConnectionMultiplexer>()
            .Should().BeSameAs(provider.GetRequiredService<IConnectionMultiplexer>());
        provider.GetRequiredService<IFixedWindowStore>()
            .Should().BeSameAs(provider.GetRequiredService<IFixedWindowStore>());
        provider.GetRequiredService<ISlidingWindowStore>()
            .Should().BeSameAs(provider.GetRequiredService<ISlidingWindowStore>());
    }

    [Fact]
    public async Task AddRedisRateLimiter_ProducesStoresThatTalkToTheConfiguredServer()
    {
        // Arrange
        using var provider = BuildProvider();

        var store = provider.GetRequiredService<IFixedWindowStore>();
        var key = $"{_keyPrefix}:{nameof(AddRedisRateLimiter_ProducesStoresThatTalkToTheConfiguredServer)}";

        // Act
        var count = await store.IncrementAsync(key, TimeSpan.FromSeconds(30));

        // Assert
        count.Should().Be(1);
        (await _fixture.Connection.GetDatabase().KeyExistsAsync(key)).Should().BeTrue();
    }

    [Fact]
    public void AddRedisRateLimiter_WithoutAKeyPrefix_FallsBackToTheDefault()
    {
        // Arrange
        using var provider = BuildProvider(omitKeyPrefix: true);

        // Act
        var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;

        // Assert
        options.KeyPrefix.Should().Be("ratelimit");
        options.Database.Should().Be(-1);
    }

    [Fact]
    public void AddRedisRateLimiter_WithABlankConnectionString_FailsValidation()
    {
        // Arrange
        using var provider = BuildProvider(connectionString: "   ");

        // Act
        var act = () => provider.GetRequiredService<IOptions<RedisOptions>>().Value;

        // Assert
        act.Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainMatch($"*{nameof(RedisOptions.ConnectionString)} must be provided*");
    }

    [Fact]
    public void AddRedisRateLimiter_WithABlankKeyPrefix_FailsValidation()
    {
        // Arrange
        using var provider = BuildProvider(keyPrefix: "   ");

        // Act
        var act = () => provider.GetRequiredService<IOptions<RedisOptions>>().Value;

        // Assert
        act.Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainMatch($"*{nameof(RedisOptions.KeyPrefix)} must be provided*");
    }

    private ServiceProvider BuildProvider(
        string? connectionString = null,
        string? keyPrefix = null,
        bool omitKeyPrefix = false)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{RedisOptions.SectionName}:{nameof(RedisOptions.ConnectionString)}"] =
                connectionString ?? _fixture.ConnectionString
        };

        if (!omitKeyPrefix)
        {
            settings[$"{RedisOptions.SectionName}:{nameof(RedisOptions.KeyPrefix)}"] = keyPrefix ?? _keyPrefix;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new ServiceCollection()
            .AddRedisRateLimiter(configuration)
            .BuildServiceProvider();
    }
}
