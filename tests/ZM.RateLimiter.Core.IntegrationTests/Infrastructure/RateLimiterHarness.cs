using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Options;

namespace ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

internal sealed class RateLimiterHarness : IDisposable
{
    private readonly ServiceProvider _services;

    public RateLimiterHarness(
        ServiceProvider services,
        FakeTimeProvider timeProvider,
        InMemoryFixedWindowStore fixedWindowStore,
        InMemorySlidingWindowStore slidingWindowStore)
    {
        _services = services;

        TimeProvider = timeProvider;
        FixedWindowStore = fixedWindowStore;
        SlidingWindowStore = slidingWindowStore;
    }

    public IServiceProvider Services => _services;

    public FakeTimeProvider TimeProvider { get; }

    public InMemoryFixedWindowStore FixedWindowStore { get; }

    public InMemorySlidingWindowStore SlidingWindowStore { get; }

    public IRateLimiter RateLimiter => _services.GetRequiredService<IRateLimiter>();

    public RateLimitingOptions Options => _services.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    public void Dispose() => _services.Dispose();
}
