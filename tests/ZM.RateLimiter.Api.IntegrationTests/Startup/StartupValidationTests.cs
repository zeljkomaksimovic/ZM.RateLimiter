using FluentAssertions;
using Microsoft.Extensions.Options;
using ZM.RateLimiter.Api.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Api.IntegrationTests.Startup;

[Collection(RedisCollection.Name)]
public sealed class StartupValidationTests
{
    private readonly RedisFixture _fixture;

    public StartupValidationTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Startup_WithAnInvalidPolicy_FailsFast()
    {
        // Arrange
        using var factory = new RateLimiterApiFactoryBuilder(_fixture, RedisFixture.CreateKeyPrefix("invalid"))
            .WithPolicy("broken", RateLimitingAlgorithmType.FixedWindow, limit: 0, window: TimeSpan.Zero)
            .Build();

        // Act
        var act = () => factory.CreateClient();

        // Assert
        // ValidateOnStart means a misconfigured deployment never accepts a single request.
        act.Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainMatch("*'broken' must have a Limit greater than zero*");
    }

    [Fact]
    public void Startup_WithAnApiKeyBoundToAMissingPolicy_FailsFast()
    {
        // Arrange
        using var factory = new RateLimiterApiFactoryBuilder(_fixture, RedisFixture.CreateKeyPrefix("dangling"))
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithApiKey("dangling-key", "ghost")
            .Build();

        // Act
        var act = () => factory.CreateClient();

        // Assert
        act.Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainMatch("*mapped to policy 'ghost', which is not configured*");
    }

    [Fact]
    public async Task Startup_WithAValidConfiguration_ServesRequests()
    {
        // Arrange
        using var factory = new RateLimiterApiFactoryBuilder(_fixture, RedisFixture.CreateKeyPrefix("valid"))
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithApiKey("valid-key", "free")
            .Build();

        using var client = factory.CreateClient();

        // Act
        var body = await client.ConsumeSuccessfullyAsync("valid-key");

        // Assert
        body.Allowed.Should().BeTrue();
        body.Policy.Should().Be("free");
    }
}
