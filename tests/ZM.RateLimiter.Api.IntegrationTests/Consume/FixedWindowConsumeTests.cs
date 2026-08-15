using System.Net;
using FluentAssertions;
using ZM.RateLimiter.Api.Contracts;
using ZM.RateLimiter.Api.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Api.IntegrationTests.Consume;

[Collection(RedisCollection.Name)]
public sealed class FixedWindowConsumeTests : IDisposable
{
    private const string ClientKey = "fixed-window-client";
    private const long Limit = 3;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly RateLimiterApiFactory _factory;
    private readonly HttpClient _client;

    public FixedWindowConsumeTests(RedisFixture fixture)
    {
        _factory = new RateLimiterApiFactoryBuilder(fixture, RedisFixture.CreateKeyPrefix("fixed"))
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, Limit, Window)
            .WithClientPolicy(ClientKey, "free")
            // Twenty seconds into a window, so retry-after is not trivially the whole window.
            .StartingAt(RateLimiterApiFactoryBuilder.DefaultStartTime.AddSeconds(20))
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
            response.Policy.Should().Be("free");
            response.Allowed.Should().BeTrue();
            response.Limit.Should().Be(Limit);
            response.RetryAfter.Should().BeNull();
        });

        responses.Select(response => response.Remaining).Should().Equal(2, 1, 0);
    }

    [Fact]
    public async Task Consume_BeyondTheLimit_ReportsTheTimeLeftInTheWindow()
    {
        // Arrange
        await ExhaustAsync();

        // Act
        var body = await _client.ConsumeSuccessfullyAsync(ClientKey);

        // Assert
        body.Allowed.Should().BeFalse();
        body.Remaining.Should().Be(0);
        body.RetryAfter.Should().Be(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public async Task Consume_BeyondTheLimit_StillAnswersWith200AndNoRetryAfterHeader()
    {
        // Arrange
        await ExhaustAsync();

        // Act
        using var response = await _client.ConsumeAsync(ClientKey);

        // Assert
        // Current behaviour: a denial is reported in the payload, not as HTTP 429 with a Retry-After
        // header. This test pins that contract so a change to it has to be deliberate.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task Consume_AfterTheWindowRolls_IsAllowedAgain()
    {
        // Arrange
        await ExhaustAsync();

        // Act
        _factory.TimeProvider.Advance(TimeSpan.FromSeconds(40));

        var body = await _client.ConsumeSuccessfullyAsync(ClientKey);

        // Assert
        body.Allowed.Should().BeTrue();
        body.Remaining.Should().Be(Limit - 1);
    }

    [Fact]
    public async Task Consume_BeforeTheWindowRolls_StaysDenied()
    {
        // Arrange
        await ExhaustAsync();

        // Act
        _factory.TimeProvider.Advance(TimeSpan.FromSeconds(39));

        var body = await _client.ConsumeSuccessfullyAsync(ClientKey);

        // Assert
        body.Allowed.Should().BeFalse();
        body.RetryAfter.Should().Be(TimeSpan.FromSeconds(1));
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
