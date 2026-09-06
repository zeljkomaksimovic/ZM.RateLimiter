# Configuration

Two configuration sections are bound: `RateLimiting` (by `AddRateLimiterCore`) and `Redis` (by
`AddRedisRateLimiter`). Both are validated with `ValidateOnStart()`, so invalid configuration throws an
`OptionsValidationException` while the host is starting.

## RateLimiting

| Key | Type | Default | Required |
| --- | --- | --- | --- |
| `RateLimiting:DefaultPolicy` | `string?` | `null` | No |
| `RateLimiting:Policies` | dictionary of policy name → policy | empty | Yes — at least one |
| `RateLimiting:Policies:<name>:Algorithm` | `FixedWindow` \| `SlidingWindow` | `FixedWindow` | No |
| `RateLimiting:Policies:<name>:Limit` | `long` | `0` | Yes — must be greater than zero |
| `RateLimiting:Policies:<name>:Window` | `TimeSpan` | `TimeSpan.Zero` | Yes — must be greater than zero |
| `RateLimiting:ClientPolicies` | dictionary of client key → policy name | empty | No |

### DefaultPolicy

Names the policy applied to a client key that has no entry in `ClientPolicies`. When it is `null` or blank,
an unmapped client key resolves to no policy at all and `ConsumeAsync` returns `null`.

The fallback covers *absent* mappings only. A client key that **is** mapped, but to a policy name that does
not exist, still resolves to nothing — it does not fall through to the default.

Each unmapped client is still metered individually. The counter is partitioned by client key regardless of
which policy resolved it, so a default policy of 60 per minute grants 60 per minute to *each* unrecognised
client, not 60 shared between them.

### Policies

The window is a `TimeSpan`, written in configuration as `"hh:mm:ss"`:

```json
"free": {
  "Algorithm": "FixedWindow",
  "Limit": 60,
  "Window": "00:01:00"
}
```

`Algorithm` binds by name and is case-insensitive. Omitting it selects `FixedWindow`. See
[Algorithms](algorithms.md) for the difference.

### ClientPolicies

```json
"ClientPolicies": {
  "demo-free-client": "free",
  "demo-pro-client": "pro"
}
```

The key is the client key your application passes to `ConsumeAsync`; the value is a policy name from
`Policies`.

## Case sensitivity

The two dictionaries use different comparers, and the difference is deliberate:

| Dictionary | Comparer | Consequence |
| --- | --- | --- |
| `Policies` | `StringComparer.OrdinalIgnoreCase` | Policy names are **case-insensitive**. A client mapped to `"FREE"` resolves the policy declared as `free`. |
| `ClientPolicies` | `StringComparer.Ordinal` | Client keys are **case-sensitive**. `Demo-Free-Client` does not match a configured `demo-free-client`. |

A client key that differs only in case counts as unmapped, which means it falls back to `DefaultPolicy` if
one is configured.

## Redis

| Key | Type | Default | Required |
| --- | --- | --- | --- |
| `Redis:ConnectionString` | `string` | `string.Empty` | Yes |
| `Redis:KeyPrefix` | `string` | `ratelimit` | Yes — must be non-blank |
| `Redis:Database` | `int` | `-1` | No |

`ConnectionString` is passed to `ConfigurationOptions.Parse`, so any StackExchange.Redis connection string
works. The connection is created with `AbortOnConnectFail = false` and `ClientName = "RateLimiter"` — the
client name is what you will see in `CLIENT LIST`.

`Database` defaults to `-1`, meaning the database selected by the connection string.

`KeyPrefix` namespaces every key written by the Redis stores. It has a usable default, so it only fails
validation if you explicitly set it to an empty or whitespace value. See
[Algorithms](algorithms.md#redis-key-layout) for exactly where it is applied.

## Environment variables

Nested keys use a double underscore as the separator:

```
Redis__ConnectionString=localhost:6379
Redis__KeyPrefix=rate-limiter
RateLimiting__DefaultPolicy=free
RateLimiting__Policies__free__Limit=60
RateLimiting__Policies__free__Window=00:01:00
RateLimiting__ClientPolicies__demo-free-client=free
```

## Startup validation

Validation failures are **accumulated, not short-circuited** — one startup reports every problem it finds,
not just the first.

From `RateLimitingOptionsValidator`:

| Message | Raised when |
| --- | --- |
| `'RateLimiting:Policies' must define at least one policy.` | `Policies` is empty |
| `A policy name must not be empty.` | A policy is declared with a blank name |
| `Policy '{name}' must have a Limit greater than zero.` | `Limit` is zero or negative |
| `Policy '{name}' must have a Window greater than zero.` | `Window` is zero or negative |
| `DefaultPolicy '{name}' does not match any configured policy.` | `DefaultPolicy` is set but names no policy |
| `A client key must not be empty.` | A `ClientPolicies` entry has a blank key |
| `A client key is mapped to policy '{name}', which is not configured.` | A `ClientPolicies` value names no policy |

The last message reports the *policy* name and deliberately omits the client key, so that a client key never
reaches your startup logs.

From `AddRedisRateLimiter`:

| Message | Raised when |
| --- | --- |
| `ConnectionString must be provided.` | `Redis:ConnectionString` is null, empty or whitespace |
| `KeyPrefix must be provided.` | `Redis:KeyPrefix` is null, empty or whitespace |

## Reload

`ConfigurationRateLimitPolicyProvider` reads `IOptionsMonitor<RateLimitingOptions>.CurrentValue` on every
lookup. If your configuration source supports reload-on-change, edits to `Policies`, `ClientPolicies` and
`DefaultPolicy` take effect on the next call without restarting the process.

`ValidateOnStart()` runs once during startup and does not re-run on reload, so a reloaded configuration is
not re-validated.
