using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.DependencyInjection;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Options;

namespace ZM.RateLimiter.Core.IntegrationTests.Infrastructure;

internal sealed class RateLimiterHarnessBuilder
{
    public static readonly DateTimeOffset DefaultStartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string?> _configuration = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset _startTime = DefaultStartTime;
    private string _keyPrefix = "test";
    private bool _registerStores = true;

    public RateLimiterHarnessBuilder WithDefaultPolicy(string? policyName)
    {
        _configuration[$"{RateLimitingOptions.SectionName}:DefaultPolicy"] = policyName;

        return this;
    }

    public RateLimiterHarnessBuilder WithPolicy(
        string name,
        RateLimitingAlgorithmType algorithm,
        long limit,
        TimeSpan window)
    {
        var section = $"{RateLimitingOptions.SectionName}:Policies:{name}";

        _configuration[$"{section}:Algorithm"] = algorithm.ToString();
        _configuration[$"{section}:Limit"] = limit.ToString(CultureInfo.InvariantCulture);
        _configuration[$"{section}:Window"] = window.ToString();

        return this;
    }

    public RateLimiterHarnessBuilder WithClientPolicy(string clientKey, string policyName)
    {
        _configuration[$"{RateLimitingOptions.SectionName}:ClientPolicies:{clientKey}"] = policyName;

        return this;
    }

    public RateLimiterHarnessBuilder WithSetting(string key, string? value)
    {
        _configuration[key] = value;

        return this;
    }

    public RateLimiterHarnessBuilder StartingAt(DateTimeOffset startTime)
    {
        _startTime = startTime;

        return this;
    }

    public RateLimiterHarnessBuilder WithKeyPrefix(string keyPrefix)
    {
        _keyPrefix = keyPrefix;

        return this;
    }

    public RateLimiterHarnessBuilder WithoutStores()
    {
        _registerStores = false;

        return this;
    }

    public RateLimiterHarness Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(_configuration)
            .Build();

        var timeProvider = new FakeTimeProvider(_startTime);
        var fixedWindowStore = new InMemoryFixedWindowStore(timeProvider);
        var slidingWindowStore = new InMemorySlidingWindowStore();

        var services = new ServiceCollection();

        services.AddSingleton<TimeProvider>(timeProvider);

        if (_registerStores)
        {
            services.AddSingleton<IRateLimitKeyGenerator>(new TestRateLimitKeyGenerator(_keyPrefix));
            services.AddSingleton<IFixedWindowStore>(fixedWindowStore);
            services.AddSingleton<ISlidingWindowStore>(slidingWindowStore);
        }

        services.AddRateLimiterCore(configuration);

        return new RateLimiterHarness(
            services.BuildServiceProvider(),
            timeProvider,
            fixedWindowStore,
            slidingWindowStore);
    }
}
