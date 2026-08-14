using System.Net.Http.Json;
using ZM.RateLimiter.Api.Contracts;

namespace ZM.RateLimiter.Api.IntegrationTests.Infrastructure;

internal static class RateLimiterClientExtensions
{
    public const string ConsumeRoute = "/api/v1/rate-limiter/consume";
    public const string ApiKeyHeaderName = "X-Api-Key";

    public static Task<HttpResponseMessage> ConsumeAsync(
        this HttpClient client,
        string? apiKey,
        string? resource = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ConsumeRoute)
        {
            Content = JsonContent.Create(new ConsumeRateLimitRequest(resource))
        };

        if (apiKey is not null)
        {
            request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, apiKey);
        }

        return client.SendAsync(request);
    }

    public static async Task<ConsumeRateLimitResponse> ConsumeSuccessfullyAsync(
        this HttpClient client,
        string apiKey,
        string? resource = null)
    {
        using var response = await client.ConsumeAsync(apiKey, resource);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ConsumeRateLimitResponse>())!;
    }
}
