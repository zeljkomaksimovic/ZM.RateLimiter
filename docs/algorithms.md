# Algorithms

Each policy selects one algorithm through `RateLimiting:Policies:<name>:Algorithm`. Both are backed by a
Lua script, so the check-and-consume is atomic on the Redis side.

## Choosing

| | Fixed window | Sliding window |
| --- | --- | --- |
| Redis structure | String counter | Sorted set |
| Work per call | One `INCR` (+ one `PEXPIRE` on creation) | `ZREMRANGEBYSCORE` + `ZCARD` + `ZADD` + `PEXPIRE` + `ZRANGE` |
| Memory per partition | One small key per active window | One member per admitted request within the window |
| Boundary behaviour | The full limit becomes available at once at each boundary | Capacity returns one entry at a time |
| Burst risk | A client can spend the limit at the end of one window and again at the start of the next | Smoothed — any window-length span is bounded by the limit |

Fixed window is cheaper and bounded in memory by the number of active partitions. Sliding window costs
memory proportional to the limit and gives a stricter guarantee. Use fixed window unless boundary bursts
matter to you.

## Fixed window

Windows are aligned to absolute time, not to a client's first request:

```csharp
var windowStart = timestamp.UtcTicks - (timestamp.UtcTicks % window.Ticks);
```

With a one-minute window, every window starts on a whole minute. Two clients that start at different moments
share the same boundaries.

Consumption increments the counter for the current window and compares it to the limit:

```csharp
return count <= policy.Limit
    ? RateLimitResult.Allowed(
        policy.Limit,
        policy.Limit - count)
    : RateLimitResult.Denied(
        policy.Limit,
        retryAfter);
```

`RetryAfter` is the distance to the next boundary:

```csharp
private static TimeSpan CalculateRetryAfter(DateTimeOffset now, TimeSpan window)
{
    return TimeSpan.FromTicks(window.Ticks - (now.UtcTicks % window.Ticks));
}
```

It is computed *before* the increment, from the clock alone, so it does not depend on the state of the
counter.

### The script

```lua
local count = redis.call('INCR', KEYS[1])

if count == 1 then
    redis.call('PEXPIRE', KEYS[1], ARGV[1])
end

return count
```

The expiry is set **only when the counter is created**. That is what bounds the key's lifetime: a rolling
`PEXPIRE` on every call would let a steady stream of requests keep one window's key alive indefinitely.
Because the key name already contains the window start, an expired key and a new window are the same event.

## Sliding window

Every admitted request is a member of a sorted set, scored by its timestamp in Unix milliseconds. A request
is admitted if the number of members still inside the window is below the limit.

`RetryAfter` is the moment the oldest member leaves the window:

```csharp
private static TimeSpan CalculateRetryAfter(DateTimeOffset? oldestTimestamp, DateTimeOffset now, TimeSpan window)
{
    if (oldestTimestamp is null)
    {
        return window;
    }

    var retryAfter = oldestTimestamp.Value + window - now;

    return retryAfter > TimeSpan.Zero
        ? retryAfter
        : TimeSpan.Zero;
}
```

An empty set reports the full window. A negative span is clamped to zero.

This is why capacity returns gradually: each expiry frees exactly one slot, so a client that spent its limit
in a burst regains it in the same pattern one window later, rather than all at once.

### The script

```lua
local key = KEYS[1]
local now = tonumber(ARGV[1])
local cutoff = tonumber(ARGV[2])
local limit = tonumber(ARGV[3])
local ttl = tonumber(ARGV[4])
local member = ARGV[5]

redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoff)

local count = redis.call('ZCARD', key)
local added = 0

if count < limit then
    redis.call('ZADD', key, now, member)
    count = count + 1
    added = 1
end

redis.call('PEXPIRE', key, ttl)

local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
local oldestScore = -1

if oldest[2] then
    oldestScore = tonumber(oldest[2])
end

return { added, count, oldestScore }
```

Step by step: drop everything older than the cutoff (`now - window`), count what is left, add the new member
only if there is room, refresh the expiry, then report the oldest remaining score so the caller can compute
retry-after. Eviction, counting and admission happen inside one script, so concurrent callers cannot
over-admit.

The expiry here **is** refreshed on every call, which is correct for this structure — the set is a single
long-lived key per partition, and the refresh is what lets an abandoned partition fall out of Redis on its
own.

Members are `{unixMillis}-{guid:N}`:

```csharp
var member = $"{now}-{Guid.NewGuid():N}";
```

The GUID suffix exists because sorted-set members are unique. Two requests arriving in the same millisecond
would otherwise collapse into a single member and one of them would go uncounted.

## Redis key layout

Both paths start from the request's composite key — the client-key digest, plus the resource when one was
supplied (see [Usage](usage.md#scoping-by-resource)).

**Fixed window**

```
{KeyPrefix}:{digest}:{windowStartTicks}
{KeyPrefix}:{digest}:{resource}:{windowStartTicks}
```

For example, `rate-limiter:9f2b1c8ad4e07356bb1190f4c2d8ae51:reports:638712345600000000`.

**Sliding window**

```
{KeyPrefix}:{digest}
{KeyPrefix}:{digest}:{resource}
```

For example, `rate-limiter:9f2b1c8ad4e07356bb1190f4c2d8ae51:reports`. There is no window suffix — the
sorted set spans windows, and time is carried in the scores.

### Which component applies KeyPrefix

The two paths differ, and it matters if you write your own store.

**Fixed window — the key generator applies it.** `FixedWindowAlgorithm` calls `IRateLimitKeyGenerator`, and
`DefaultRateLimitKeyGenerator` produces a fully-qualified key:

```csharp
return $"{_options.KeyPrefix}:{request.CompositeKey}:{windowStart}";
```

`RedisFixedWindowStore` then uses that key verbatim. It must not prefix again.

**Sliding window — the store applies it.** `SlidingWindowAlgorithm` never touches the key generator; it
passes `request.CompositeKey` straight through, and `RedisSlidingWindowStore` prefixes it when calling the
script:

```csharp
var result = await database.ScriptEvaluateAsync(
    ConsumeScript,
    [$"{_options.KeyPrefix}:{key}"],
    [now, now - windowMilliseconds, limit, windowMilliseconds, member]);
```

So an `IFixedWindowStore` receives a prefixed key, and an `ISlidingWindowStore` receives an unprefixed one.
Both behaviours are pinned by tests in `tests/ZM.RateLimiter.Redis.IntegrationTests/Stores`.
