using System.Net;
using System.Text.Json;
using FluentAssertions;
using ZM.RateLimiter.Api.IntegrationTests.Infrastructure;
using ZM.RateLimiter.Core.Enums;

namespace ZM.RateLimiter.Api.IntegrationTests.Startup;

[Collection(RedisCollection.Name)]
public sealed class SwaggerDocumentTests : IDisposable
{
    private readonly RateLimiterApiFactory _factory;
    private readonly HttpClient _client;

    public SwaggerDocumentTests(RedisFixture fixture)
    {
        _factory = new RateLimiterApiFactoryBuilder(fixture, RedisFixture.CreateKeyPrefix("swagger"))
            .WithPolicy("free", RateLimitingAlgorithmType.FixedWindow, limit: 5, window: TimeSpan.FromMinutes(1))
            .WithApiKey("swagger-key", "free")
            .Build();

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task SwaggerDocument_DescribesTheConsumeOperation()
    {
        // Act
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(RateLimiterClientExtensions.ConsumeRoute)
            .GetProperty("post");

        operation.GetProperty("operationId").GetString().Should().Be("ConsumeRateLimit");
        operation.GetProperty("responses").TryGetProperty("200", out _).Should().BeTrue();
        operation.GetProperty("responses").TryGetProperty("401", out _).Should().BeTrue();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
