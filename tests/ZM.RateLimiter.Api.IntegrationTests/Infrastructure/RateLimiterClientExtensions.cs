using System.Net.Http.Json;
using ZM.RateLimiter.Api.Contracts;

namespace ZM.RateLimiter.Api.IntegrationTests.Infrastructure;

internal static class RateLimiterClientExtensions
{
    public const string ConsumeRoute = "/api/v1/rate-limiter/consume";
    public const string ClientKeyHeaderName = "X-Client-Key";

    public static Task<HttpResponseMessage> ConsumeAsync(
        this HttpClient client,
        string? clientKey,
        string? resource = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ConsumeRoute)
        {
            Content = JsonContent.Create(new ConsumeRateLimitRequest(resource))
        };

        if (clientKey is not null)
        {
            request.Headers.TryAddWithoutValidation(ClientKeyHeaderName, clientKey);
        }

        return client.SendAsync(request);
    }

    public static async Task<ConsumeRateLimitResponse> ConsumeSuccessfullyAsync(
        this HttpClient client,
        string clientKey,
        string? resource = null)
    {
        using var response = await client.ConsumeAsync(clientKey, resource);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ConsumeRateLimitResponse>())!;
    }
}
