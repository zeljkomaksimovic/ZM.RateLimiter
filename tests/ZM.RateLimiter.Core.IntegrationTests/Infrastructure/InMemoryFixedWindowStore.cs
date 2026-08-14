using System.Collections.Concurrent;
using ZM.RateLimiter.Core.Abstractions;

namespace ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

internal sealed class InMemoryFixedWindowStore : IFixedWindowStore
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _observedKeys = new();

    public InMemoryFixedWindowStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    public IReadOnlyList<string> ObservedKeys => [.. _observedKeys];

    public Task<long> IncrementAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeToLive, TimeSpan.Zero);

        cancellationToken.ThrowIfCancellationRequested();

        _observedKeys.Enqueue(key);

        var now = _timeProvider.GetUtcNow();

        var entry = _entries.AddOrUpdate(
            key,
            _ => new Entry(1, now + timeToLive),
            (_, existing) => existing.ExpiresAt <= now
                ? new Entry(1, now + timeToLive)
                : existing with { Count = existing.Count + 1 });

        return Task.FromResult(entry.Count);
    }

    private sealed record Entry(long Count, DateTimeOffset ExpiresAt);
}
