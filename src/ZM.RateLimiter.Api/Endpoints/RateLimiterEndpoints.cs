using Carter;
using Microsoft.AspNetCore.Mvc;
using ZM.RateLimiter.Api.Contracts;
using ZM.RateLimiter.Core.Abstractions;

namespace ZM.RateLimiter.Api.Endpoints;

public sealed class RateLimiterEndpoints : ICarterModule
{
    private const string ClientKeyHeaderName = "X-Client-Key";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/rate-limiter")
            .WithTags("Rate Limiter");

        group.MapPost("/consume", HandleConsumeAsync)
            .WithName("ConsumeRateLimit")
            .WithSummary("Consumes one request against the policy bound to the supplied client key, or the default policy when one is configured.")
            .Produces<ConsumeRateLimitResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> HandleConsumeAsync(
        [FromHeader(Name = ClientKeyHeaderName)] string? clientKey,
        ConsumeRateLimitRequest? request,
        IRateLimiter rateLimiter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return Results.Problem(
                title: "Missing client key.",
                detail: $"The '{ClientKeyHeaderName}' header is required.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var outcome = await rateLimiter.ConsumeAsync(
            clientKey,
            request?.Resource,
            cancellationToken);

        if (outcome is null)
        {
            return Results.Problem(
                title: "Unknown client key.",
                detail: "The supplied client key is not associated with a rate limiting policy.",
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
