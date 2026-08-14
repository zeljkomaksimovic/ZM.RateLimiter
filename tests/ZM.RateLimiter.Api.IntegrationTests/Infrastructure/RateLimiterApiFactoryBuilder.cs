using System.Globalization;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Options;
using ZM.RateLimiter.Redis.Options;

namespace ZM.RateLimiter.Api.IntegrationTests.Infrastructure;

internal sealed class RateLimiterApiFactoryBuilder
{
    // A whole number of days is an exact multiple of any sub-hour window, which keeps window
    // boundaries predictable when a test asserts on retry-after values.
    public static readonly DateTimeOffset DefaultStartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset _startTime = DefaultStartTime;

    public RateLimiterApiFactoryBuilder(RedisFixture fixture, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _settings[$"{RedisOptions.SectionName}:{nameof(RedisOptions.ConnectionString)}"] = fixture.ConnectionString;
        _settings[$"{RedisOptions.SectionName}:{nameof(RedisOptions.KeyPrefix)}"] = keyPrefix;
        _settings[$"{RedisOptions.SectionName}:{nameof(RedisOptions.Database)}"] = "0";
    }

    public RateLimiterApiFactoryBuilder WithPolicy(
        string name,
        RateLimitingAlgorithmType algorithm,
        long limit,
        TimeSpan window)
    {
        var section = $"{RateLimitingOptions.SectionName}:Policies:{name}";

        _settings[$"{section}:Algorithm"] = algorithm.ToString();
        _settings[$"{section}:Limit"] = limit.ToString(CultureInfo.InvariantCulture);
        _settings[$"{section}:Window"] = window.ToString();

        return this;
    }

    public RateLimiterApiFactoryBuilder WithApiKey(string apiKey, string policyName)
    {
        _settings[$"{RateLimitingOptions.SectionName}:ApiKeys:{apiKey}"] = policyName;

        return this;
    }

    public RateLimiterApiFactoryBuilder StartingAt(DateTimeOffset startTime)
    {
        _startTime = startTime;

        return this;
    }

    public RateLimiterApiFactory Build() => new(_settings, _startTime);
}
