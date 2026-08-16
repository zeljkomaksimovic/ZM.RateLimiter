using FluentAssertions;
using ZM.RateLimiter.Api.Contracts;
using ZM.RateLimiter.Api.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Api.IntegrationTests.Consume;

[Collection(RedisCollection.Name)]
public sealed class SlidingWindowConsumeTests : IDisposable
{
    private const string ClientKey = "sliding-window-client";
    private const long Limit = 3;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly RateLimiterApiFactory _factory;
    private readonly HttpClient _client;

    public SlidingWindowConsumeTests(RedisFixture fixture)
    {
        _factory = new RateLimiterApiFactoryBuilder(fixture, RedisFixture.CreateKeyPrefix("sliding"))
            .WithPolicy("pro", RateLimitingAlgorithmType.SlidingWindow, Limit, Window)
            .WithClientPolicy(ClientKey, "pro")
            .Build();

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Consume_UpToTheLimit_CountsRemainingDown()
    {
        // Act
        var responses = new List<ConsumeRateLimitResponse>();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            responses.Add(await _client.ConsumeSuccessfullyAsync(ClientKey));
        }

        // Assert
        responses.Should().AllSatisfy(response =>
        {
            response.Policy.Should().Be("pro");
            response.Allowed.Should().BeTrue();
            response.RetryAfter.Should().BeNull();
        });

        responses.Select(response => response.Remaining).Should().Equal(2, 1, 0);
    }

    [Fact]
    public async Task Consume_BeyondTheLimit_ReportsWhenTheOldestEntryExpires()
    {
        // Arrange
        await ExhaustAsync();

        // Act
        var immediately = await _client.ConsumeSuccessfullyAsync(ClientKey);

        _factory.TimeProvider.Advance(TimeSpan.FromSeconds(25));

        var later = await _client.ConsumeSuccessfullyAsync(ClientKey);

        // Assert
        immediately.Allowed.Should().BeFalse();
        immediately.Remaining.Should().Be(0);
        immediately.RetryAfter.Should().Be(Window);

        later.Allowed.Should().BeFalse();
        later.RetryAfter.Should().Be(TimeSpan.FromSeconds(35));
    }

    [Fact]
    public async Task Consume_OnceTheOldestEntryExpires_IsAllowedAgain()
    {
        // Arrange
        await ExhaustAsync();

        // Act
        _factory.TimeProvider.Advance(Window);

        var body = await _client.ConsumeSuccessfullyAsync(ClientKey);

        // Assert
        body.Allowed.Should().BeTrue();
        body.Remaining.Should().Be(Limit - 1);
    }

    [Fact]
    public async Task Consume_ReleasesCapacityGraduallyRatherThanAllAtOnce()
    {
        // Arrange
        await _client.ConsumeSuccessfullyAsync(ClientKey);
        _factory.TimeProvider.Advance(TimeSpan.FromSeconds(20));
        await _client.ConsumeSuccessfullyAsync(ClientKey);
        _factory.TimeProvider.Advance(TimeSpan.FromSeconds(20));
        await _client.ConsumeSuccessfullyAsync(ClientKey);

        // Act
        _factory.TimeProvider.Advance(TimeSpan.FromSeconds(20));

        var reclaimed = await _client.ConsumeSuccessfullyAsync(ClientKey);
        var stillLimited = await _client.ConsumeSuccessfullyAsync(ClientKey);

        // Assert
        reclaimed.Allowed.Should().BeTrue();
        reclaimed.Remaining.Should().Be(0);
        stillLimited.Allowed.Should().BeFalse();
        stillLimited.RetryAfter.Should().Be(TimeSpan.FromSeconds(20));
    }

    private async Task ExhaustAsync()
    {
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await _client.ConsumeSuccessfullyAsync(ClientKey);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
