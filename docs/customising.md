# Customising

## The rule that makes this work

`AddRateLimiterCore` and `AddRedisRateLimiter` register their services with `TryAddSingleton`, which is a
no-op when the service is already registered. **Anything you register before calling them wins.**

```csharp
builder.Services.AddSingleton<IRateLimitPolicyProvider, DatabasePolicyProvider>();
builder.Services.AddRateLimiterCore(builder.Configuration);
```

Order matters. Register after the extension and yours is ignored — the built-in registration is already
there.

## What each extension registers

`AddRateLimiterCore`:

| Service | Implementation | Registration |
| --- | --- | --- |
| `RateLimitingOptions` | bound from `RateLimiting`, `ValidateOnStart()` | `AddOptions` |
| `IValidateOptions<RateLimitingOptions>` | `RateLimitingOptionsValidator` | `TryAddEnumerable` |
| `TimeProvider` | `TimeProvider.System` | `TryAddSingleton` |
| `IRateLimiterAlgorithm` | `FixedWindowAlgorithm`, `SlidingWindowAlgorithm` | `TryAddEnumerable` |
| `IRateLimitingAlgorithmFactory` | `RateLimitingAlgorithmFactory` | `TryAddSingleton` |
| `IRateLimitPolicyProvider` | `ConfigurationRateLimitPolicyProvider` | `TryAddSingleton` |
| `IRateLimiter` | `RateLimiterService` | `TryAddSingleton` |

`AddRedisRateLimiter`:

| Service | Implementation | Registration |
| --- | --- | --- |
| `RedisOptions` | bound from `Redis`, `ValidateOnStart()` | `AddOptions` |
| `IConnectionMultiplexer` | `ConnectionMultiplexer.Connect(...)` | `TryAddSingleton` |
| `IRateLimitKeyGenerator` | `DefaultRateLimitKeyGenerator` | `TryAddSingleton` |
| `IFixedWindowStore` | `RedisFixedWindowStore` | `TryAddSingleton` |
| `ISlidingWindowStore` | `RedisSlidingWindowStore` | `TryAddSingleton` |

Core supplies no store and no key generator. Composing Core alone and resolving an algorithm fails.

## Replacing the policy source

To resolve policies from a database, a feature flag service or a cache instead of `IConfiguration`,
implement `IRateLimitPolicyProvider`:

```csharp
namespace ZM.RateLimiter.Core.Abstractions;

public interface IRateLimitPolicyProvider
{
    ValueTask<RateLimitPolicy?> GetPolicyAsync(string clientKey, CancellationToken cancellationToken = default);
}
```

Return `null` for a client you do not recognise; `RateLimiterService` turns that into a `null` outcome
without touching storage. Returning a policy means it applies, so any default-policy behaviour of your own
belongs inside this method.

`RateLimitPolicy` is `(string Name, RateLimitingAlgorithmType Algorithm, long Limit, TimeSpan Window)`. The
`Name` you return is what surfaces on `RateLimitOutcome.Policy.Name`.

Registering your own provider does not disable the `RateLimiting` section — `AddRateLimiterCore` still binds
and validates it, so it must still contain at least one valid policy. If you want nothing to do with that
section, register the pieces you need yourself rather than calling the extension.

## Replacing the storage

Skip `AddRedisRateLimiter` and register three services:

```csharp
public interface IFixedWindowStore
{
    Task<long> IncrementAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default);
}

public interface ISlidingWindowStore
{
    Task<SlidingWindowStoreResult> ConsumeAsync(
        string key,
        DateTimeOffset timestamp,
        TimeSpan window,
        long limit,
        CancellationToken cancellationToken = default);
}

public interface IRateLimitKeyGenerator
{
    string CreateWindowKey(RateLimitRequest request, DateTimeOffset timestamp, TimeSpan window);
}
```

`IncrementAsync` must return the count **after** the increment, and must apply `timeToLive` such that the key
does not outlive its window. `ConsumeAsync` returns
`SlidingWindowStoreResult(bool Added, long Count, DateTimeOffset? OldestTimestamp)` — `Added` is the
admission decision, `Count` is the number of entries in the window after any addition, and `OldestTimestamp`
is `null` when the window is empty.

Two things to carry over from the Redis implementation:

- **Atomicity.** The sliding window's evict-count-add sequence must be atomic, or concurrent callers will
  over-admit. The Redis store does it in one Lua script.
- **Prefixing.** A fixed-window store receives an already-qualified key from the key generator and must use
  it verbatim; a sliding-window store receives the bare composite key and applies its own namespacing. See
  [Algorithms](algorithms.md#which-component-applies-keyprefix).

You only need both stores if your policies use both algorithms. An application that only ever configures
`FixedWindow` never resolves `ISlidingWindowStore`.

## Controlling the clock

`TimeProvider` is registered with `TryAddSingleton(TimeProvider.System)`, so registering your own first makes
window boundaries deterministic:

```csharp
services.AddSingleton<TimeProvider>(new FakeTimeProvider(startTime));
services.AddRateLimiterCore(configuration);
```

`tests/ZM.RateLimiter.Core.IntegrationTests/Infrastructure/RateLimiterHarnessBuilder.cs` does exactly this
and is a working reference.

Pick a start time that is a whole multiple of your window when you assert on retry-after — fixed windows
align to absolute time, so an arbitrary start puts you at an arbitrary offset into a window.

## Algorithms

Algorithms are registered with `TryAddEnumerable` and collected by the factory:

```csharp
public RateLimitingAlgorithmFactory(IEnumerable<IRateLimiterAlgorithm> algorithms)
{
    ArgumentNullException.ThrowIfNull(algorithms);

    _algorithms = algorithms.ToDictionary(algorithm => algorithm.Type);
}
```

Two consequences follow from that `ToDictionary`:

- **Replacing a built-in algorithm means removing its registration**, not adding alongside it. Two
  implementations reporting the same `Type` make `ToDictionary` throw during construction. Remove the
  descriptor from the `IServiceCollection` before adding your own.
- **A genuinely new algorithm needs a new enum member.** `IRateLimiterAlgorithm.Type` is a
  `RateLimitingAlgorithmType`, which declares only `FixedWindow` and `SlidingWindow`. A third algorithm
  cannot be registered from outside the Core package without extending that enum.

Resolving a type with no registered implementation throws:

```
No rate limiting algorithm registered for '{algorithm}'.
```
