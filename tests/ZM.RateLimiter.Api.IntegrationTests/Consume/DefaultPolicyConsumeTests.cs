using System.Net;
using FluentAssertions;
using ZM.RateLimiter.Api.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Api.IntegrationTests.Consume;

[Collection(RedisCollection.Name)]
public sealed class DefaultPolicyConsumeTests : IDisposable
{
    private const string DefaultPolicyName = "anonymous";
    private const string MappedClientKey = "default-policy-mapped-client";
    private const long DefaultLimit = 2;
    private const long MappedLimit = 50;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly RateLimiterApiFactory _factory;
    private readonly HttpClient _client;

    public DefaultPolicyConsumeTests(RedisFixture fixture)
    {
        _factory = new RateLimiterApiFactoryBuilder(fixture, RedisFixture.CreateKeyPrefix("default-policy"))
            .WithPolicy(DefaultPolicyName, RateLimitingAlgorithmType.FixedWindow, DefaultLimit, Window)
            .WithPolicy("premium", RateLimitingAlgorithmType.FixedWindow, MappedLimit, Window)
            .WithClientPolicy(MappedClientKey, "premium")
            .WithDefaultPolicy(DefaultPolicyName)
            .Build();

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Consume_WithAnUnmappedClientKey_SucceedsUnderTheDefaultPolicy()
    {
        // Act
        var body = await _client.ConsumeSuccessfullyAsync("walk-in-client");

        // Assert
        body.Policy.Should().Be(DefaultPolicyName);
        body.Allowed.Should().BeTrue();
        body.Limit.Should().Be(DefaultLimit);
        body.Remaining.Should().Be(DefaultLimit - 1);
    }

    [Fact]
    public async Task Consume_WithAMappedClientKey_PrefersTheMappedPolicy()
    {
        // Act
        var body = await _client.ConsumeSuccessfullyAsync(MappedClientKey);

        // Assert
        body.Policy.Should().Be("premium");
        body.Limit.Should().Be(MappedLimit);
    }

    [Fact]
    public async Task Consume_WithDifferentUnmappedClientKeys_KeepsIndependentCounters()
    {
        // Act
        var first = await _client.ConsumeSuccessfullyAsync("stranger-one");
        var firstAgain = await _client.ConsumeSuccessfullyAsync("stranger-one");
        var other = await _client.ConsumeSuccessfullyAsync("stranger-two");

        // Assert
        first.Remaining.Should().Be(DefaultLimit - 1);
        firstAgain.Remaining.Should().Be(DefaultLimit - 2);
        other.Remaining.Should().Be(DefaultLimit - 1);
    }

    [Fact]
    public async Task Consume_BeyondTheDefaultLimit_IsDenied()
    {
        // Arrange
        const string clientKey = "exhausting-client";

        for (var attempt = 0; attempt < DefaultLimit; attempt++)
        {
            await _client.ConsumeSuccessfullyAsync(clientKey);
        }

        // Act
        var body = await _client.ConsumeSuccessfullyAsync(clientKey);

        // Assert
        body.Allowed.Should().BeFalse();
        body.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task Consume_WithoutTheClientKeyHeader_IsStillUnauthorized()
    {
        // Act
        using var response = await _client.ConsumeAsync(clientKey: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
