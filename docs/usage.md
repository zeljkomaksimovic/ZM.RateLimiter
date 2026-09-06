# Usage

## The entry point

`IRateLimiter` is the only interface you need to consume the library.

```csharp
namespace ZM.RateLimiter.Core.Abstractions;

public interface IRateLimiter
{
    Task<RateLimitOutcome?> ConsumeAsync(string clientKey, string? resource = null, CancellationToken cancellationToken = default);
}
```

It is registered as a singleton by `AddRateLimiterCore`, so inject it anywhere.

Calling `ConsumeAsync` **consumes** a request: the counter is incremented before the decision is returned.
Call it once per request you intend to meter, and use the returned decision — do not call it again to
re-check.

## Handling the three outcomes

```csharp
RateLimitOutcome? outcome;

try
{
    outcome = await rateLimiter.ConsumeAsync(clientKey, resource, cancellationToken);
}
catch (ArgumentException)
{
    // clientKey was null, empty or whitespace.
    return Unauthenticated();
}

if (outcome is null)
{
    // No policy resolved for this client key.
    return Unauthorised();
}

if (!outcome.Result.IsAllowed)
{
    return Throttled(outcome.Result.RetryAfter);
}

return Proceed();
```

| Outcome | Meaning |
| --- | --- |
| Throws `ArgumentException` | `clientKey` was null, empty or whitespace. Nothing was consumed. |
| Returns `null` | The client key resolved to no policy — it is unmapped and no `DefaultPolicy` is configured, or it is mapped to a policy name that does not exist. Nothing was consumed and no storage was touched. |
| Returns a `RateLimitOutcome` | A policy applied. Inspect `Result.IsAllowed` for the decision. |

A `null` result is a *resolution* failure, not a throttling decision. Being over the limit is expressed by
`IsAllowed == false` on a non-null outcome.

## The models

```csharp
public sealed record RateLimitOutcome(RateLimitPolicy Policy, RateLimitResult Result);

public sealed record RateLimitPolicy(string Name, RateLimitingAlgorithmType Algorithm, long Limit, TimeSpan Window);

public sealed record RateLimitResult(bool IsAllowed, long Limit, long Remaining, TimeSpan? RetryAfter);
```

`Policy` describes what applied — useful for logging, or for telling a caller which tier they are on.
`Policy.Name` is the policy name as written in the configuration that resolved it.

`Result` is the decision. Two invariants come from its factory methods:

```csharp
public static RateLimitResult Allowed(long limit, long remaining) =>
    new RateLimitResult(true, limit, remaining, RetryAfter: null);

public static RateLimitResult Denied(long limit, TimeSpan retryAfter) =>
    new RateLimitResult(false, limit, Remaining: 0, retryAfter);
```

- When `IsAllowed` is `true`, `RetryAfter` is always `null`.
- When `IsAllowed` is `false`, `Remaining` is always `0`.
- `Remaining` is never negative. The allowed branch is only taken while the count is within the limit, and
  denial forces it to zero.

`RetryAfter` is how long until capacity is expected to be available. The two algorithms compute it
differently — see [Algorithms](algorithms.md).

## Scoping by resource

The optional second argument partitions the counter within a client:

```csharp
await rateLimiter.ConsumeAsync(clientKey);            // shared bucket
await rateLimiter.ConsumeAsync(clientKey, "reports"); // independent bucket
await rateLimiter.ConsumeAsync(clientKey, "exports"); // independent bucket
```

Internally the request carries a composite key:

```csharp
public sealed record RateLimitRequest(string Key, string? Resource = null)
{
    public string CompositeKey => string.IsNullOrWhiteSpace(Resource) ? Key : $"{Key}:{Resource}";
}
```

A null, empty or whitespace resource shares one bucket per client; every distinct resource string gets its
own counter under the same policy. The limit applies per bucket, so a client on a 60-per-minute policy
calling three distinct resources may make 180 requests per minute in total.

Resource values are matched by exact string equality — `"reports"` and `"Reports"` are separate buckets.

## Client keys are not stored

The raw client key never reaches Redis. `RateLimiterService` partitions on a truncated digest of it:

```csharp
private static string CreatePartitionKey(string clientKey)
{
    var digest = SHA256.HashData(Encoding.UTF8.GetBytes(clientKey));

    return Convert.ToHexStringLower(digest.AsSpan(0, 16));
}
```

That is the first 16 bytes of the SHA-256 hash, lowercase hex — a stable 32-character string. The same
client key always produces the same partition, across processes and restarts, so a client key can be a
secret without that secret appearing in your storage layer or in anything that enumerates keys.

## Responding to a denial

The library expresses a decision; how you act on it is yours. The
[sample API](../src/ZM.RateLimiter.Api/Endpoints/RateLimiterEndpoints.cs) returns `200 OK` with
`"allowed": false` in the body and reports `retryAfter` as a field, leaving enforcement to its caller.
Returning `429 Too Many Requests` with a `Retry-After` header from `outcome.Result.RetryAfter` is equally
valid, and is the more conventional choice when you are the one enforcing.
