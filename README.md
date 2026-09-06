# ZM.RateLimiter

A distributed rate limiter for .NET 10. Policies are declared in `IConfiguration`, clients are identified by
an opaque client key, and counters live in Redis so that limits hold across every instance of your
application. Two algorithms ship in the box: a fixed window and a sliding window.

## Packages

| Package | Version | What it contains |
| --- | --- | --- |
| `ZM.RateLimiter.Core` | 2.1.0 | Abstractions, both algorithms, policy resolution, options binding and validation. No Redis dependency. |
| `ZM.RateLimiter.Redis` | 2.1.0 | Redis storage for both algorithms, built on StackExchange.Redis. |

Both target `net10.0`. Both projects set `GeneratePackageOnBuild`, so `dotnet build` writes the `.nupkg`
files into each project's output folder — reference them from a local feed, or add the projects directly.

**Core alone is not functional.** It registers no `IFixedWindowStore`, `ISlidingWindowStore` or
`IRateLimitKeyGenerator`; a storage package supplies those. Without them, resolving `IRateLimiter` throws,
because the algorithms it depends on cannot be constructed. Use `ZM.RateLimiter.Redis`, or provide your own —
see [Customising](docs/customising.md).

## Quick start

### 1. Register the services

```csharp
using ZM.RateLimiter.Core.DependencyInjection;
using ZM.RateLimiter.Redis.DependencyInjection;

builder.Services.AddRateLimiterCore(builder.Configuration);
builder.Services.AddRedisRateLimiter(builder.Configuration);
```

Both extensions bind and validate their options with `ValidateOnStart()`, so a misconfigured application
fails at startup rather than on the first request.

### 2. Configure policies and clients

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "Database": 0,
    "KeyPrefix": "rate-limiter"
  },
  "RateLimiting": {
    "DefaultPolicy": null,
    "Policies": {
      "free": {
        "Algorithm": "FixedWindow",
        "Limit": 60,
        "Window": "00:01:00"
      },
      "pro": {
        "Algorithm": "SlidingWindow",
        "Limit": 1000,
        "Window": "00:01:00"
      }
    },
    "ClientPolicies": {
      "demo-free-client": "free",
      "demo-pro-client": "pro"
    }
  }
}
```

`ClientPolicies` maps a client key to a policy name. A client key with no mapping falls back to
`DefaultPolicy` when one is set, and otherwise resolves to nothing. Full reference:
[Configuration](docs/configuration.md).

### 3. Consume

Inject `IRateLimiter` and call it once per request you want to meter:

```csharp
using ZM.RateLimiter.Core.Abstractions;

public sealed class OrderService(IRateLimiter rateLimiter)
{
    public async Task<bool> TryPlaceOrderAsync(string clientKey, CancellationToken cancellationToken)
    {
        var outcome = await rateLimiter.ConsumeAsync(clientKey, "orders", cancellationToken);

        if (outcome is null)
        {
            return false;
        }

        if (!outcome.Result.IsAllowed)
        {
            return false;
        }

        return true;
    }
}
```

`ConsumeAsync` returns `null` when the client key resolves to no policy, and otherwise a `RateLimitOutcome`
carrying the policy that applied and the decision. Calling it **consumes** a request — the counter is
incremented before the decision comes back. Full reference: [Usage](docs/usage.md).

## Worked example

[`src/ZM.RateLimiter.Api`](src/ZM.RateLimiter.Api) is a small ASP.NET Core service that wires the two
packages together and exposes the decision over HTTP.

[`DependencyInjection/ServiceCollectionExtensions.cs`](src/ZM.RateLimiter.Api/DependencyInjection/ServiceCollectionExtensions.cs)
shows the registration, and
[`Endpoints/RateLimiterEndpoints.cs`](src/ZM.RateLimiter.Api/Endpoints/RateLimiterEndpoints.cs) shows how one
caller chose to turn a `RateLimitOutcome?` into a response — reading the client key from an `X-Client-Key`
header, rejecting a missing key and an unresolvable one differently, and projecting the result onto a
response record:

```csharp
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
```

How you surface a denial is your decision; the library only expresses one.

## Documentation

| Page | Covers |
| --- | --- |
| [Configuration](docs/configuration.md) | Every key, its default, startup validation, reload behaviour |
| [Usage](docs/usage.md) | `IRateLimiter`, the result models, resource scoping, error cases |
| [Algorithms](docs/algorithms.md) | Fixed vs sliding semantics, retry-after, Redis key layout |
| [Customising](docs/customising.md) | Replacing the policy source, the stores, the clock |
