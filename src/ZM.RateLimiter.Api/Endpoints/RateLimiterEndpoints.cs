using Carter;
using Microsoft.AspNetCore.Mvc;
using ZM.RateLimiter.Api.Contracts;
using ZM.RateLimiter.Core.Abstractions;

namespace ZM.RateLimiter.Api.Endpoints;

public sealed class RateLimiterEndpoints : ICarterModule
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/rate-limiter")
            .WithTags("Rate Limiter");

        group.MapPost("/consume", HandleConsumeAsync)
            .WithName("ConsumeRateLimit")
            .WithSummary("Consumes one request against the policy bound to the supplied API key.")
            .Produces<ConsumeRateLimitResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> HandleConsumeAsync(
        [FromHeader(Name = ApiKeyHeaderName)] string? apiKey,
        ConsumeRateLimitRequest? request,
        IRateLimiter rateLimiter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Results.Problem(
                title: "Missing API key.",
                detail: $"The '{ApiKeyHeaderName}' header is required.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var outcome = await rateLimiter.ConsumeAsync(
            apiKey,
            request?.Resource,
            cancellationToken);

        if (outcome is null)
        {
            return Results.Problem(
                title: "Unknown API key.",
                detail: "The supplied API key is not associated with a rate limiting policy.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(new ConsumeRateLimitResponse(
            outcome.Policy.Name,
            outcome.Result.IsAllowed,
            outcome.Result.Limit,
            outcome.Result.Remaining,
            outcome.Result.RetryAfter));
    }
}
