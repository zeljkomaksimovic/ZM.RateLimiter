using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using ZM.RateLimiter.Api.Contracts;
using ZM.RateLimiter.Api.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Api.IntegrationTests.Consume;

[Collection(RedisCollection.Name)]
public sealed class ConsumeEndpointContractTests : IDisposable
{
    private const string ApiKey = "contract-free-key";
    private const long Limit = 5;

    private readonly RateLimiterApiFactory _factory;
    private readonly HttpClient _client;

    public ConsumeEndpointContractTests(RedisFixture fixture)
    {
        _factory = new RateLimiterApiFactoryBuilder(fixture, RedisFixture.CreateKeyPrefix("contract"))
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, Limit, TimeSpan.FromMinutes(1))
            .WithApiKey(ApiKey, "free")
            .Build();

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Consume_WithoutTheApiKeyHeader_ReturnsUnauthorized()
    {
        // Act
        using var response = await _client.ConsumeAsync(apiKey: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        problem!.Title.Should().Be("Missing API key.");
        problem.Status.Should().Be((int)HttpStatusCode.Unauthorized);
        problem.Detail.Should().Contain(RateLimiterClientExtensions.ApiKeyHeaderName);
    }

    [Fact]
    public async Task Consume_WithABlankApiKeyHeader_ReturnsUnauthorized()
    {
        // Act
        using var response = await _client.ConsumeAsync("   ");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        problem!.Title.Should().Be("Missing API key.");
    }

    [Fact]
    public async Task Consume_WithAnUnmappedApiKey_ReturnsUnauthorized()
    {
        // Act
        using var response = await _client.ConsumeAsync("not-a-configured-key");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        problem!.Title.Should().Be("Unknown API key.");
        problem.Detail.Should().Contain("not associated with a rate limiting policy");
    }

    [Fact]
    public async Task Consume_WithAMappedApiKey_ReturnsThePolicySnapshot()
    {
        // Act
        var body = await _client.ConsumeSuccessfullyAsync(ApiKey);

        // Assert
        body.Should().BeEquivalentTo(new ConsumeRateLimitResponse(
            Policy: "free",
            Allowed: true,
            Limit: Limit,
            Remaining: Limit - 1,
            RetryAfter: null));
    }

    [Fact]
    public async Task Consume_WithoutARequestBody_IsAccepted()
    {
        // Arrange
        // The endpoint takes a nullable request record, so an absent body must bind to null.
        using var request = new HttpRequestMessage(HttpMethod.Post, RateLimiterClientExtensions.ConsumeRoute);

        request.Headers.TryAddWithoutValidation(RateLimiterClientExtensions.ApiKeyHeaderName, ApiKey);

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ConsumeRateLimitResponse>();

        body!.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Consume_WithDifferentResources_KeepsIndependentCounters()
    {
        // Act
        var reports = await _client.ConsumeSuccessfullyAsync(ApiKey, "reports");
        var reportsAgain = await _client.ConsumeSuccessfullyAsync(ApiKey, "reports");
        var exports = await _client.ConsumeSuccessfullyAsync(ApiKey, "exports");
        var unscoped = await _client.ConsumeSuccessfullyAsync(ApiKey);

        // Assert
        reports.Remaining.Should().Be(Limit - 1);
        reportsAgain.Remaining.Should().Be(Limit - 2);
        exports.Remaining.Should().Be(Limit - 1);
        unscoped.Remaining.Should().Be(Limit - 1);
    }

    [Fact]
    public async Task Consume_OnlyAcceptsPost()
    {
        // Act
        using var response = await _client.GetAsync(RateLimiterClientExtensions.ConsumeRoute);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
