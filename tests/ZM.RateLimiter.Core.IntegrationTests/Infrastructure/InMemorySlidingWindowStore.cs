using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Stores;

namespace ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

internal sealed class InMemorySlidingWindowStore : ISlidingWindowStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _observedKeys = [];

    public IReadOnlyList<string> ObservedKeys
    {
        get
        {
            lock (_gate)
            {
                return [.. _observedKeys];
            }
        }
    }

    public Task<SlidingWindowStoreResult> ConsumeAsync(
        string key,
        DateTimeOffset timestamp,
        TimeSpan window,
        long limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _observedKeys.Add(key);

            if (!_entries.TryGetValue(key, out var timestamps))
            {
                timestamps = [];
                _entries[key] = timestamps;
            }

            var cutoff = timestamp - window;

            timestamps.RemoveAll(entry => entry <= cutoff);

            var added = timestamps.Count < limit;

            if (added)
            {
                timestamps.Add(timestamp);
            }

            DateTimeOffset? oldest = timestamps.Count == 0
                ? null
                : timestamps.Min();

            return Task.FromResult(new SlidingWindowStoreResult(added, timestamps.Count, oldest));
        }
    }
}
