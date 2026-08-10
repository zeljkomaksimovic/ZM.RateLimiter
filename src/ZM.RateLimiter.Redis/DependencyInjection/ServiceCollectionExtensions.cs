using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Redis.KeyGeneration;
using ZM.RateLimiter.Redis.Options;
using ZM.RateLimiter.Redis.Stores;

namespace ZM.RateLimiter.Redis.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRedisRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"{nameof(RedisOptions.ConnectionString)} must be provided.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.KeyPrefix),
                $"{nameof(RedisOptions.KeyPrefix)} must be provided.")
            .ValidateOnStart();

        services.TryAddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<RedisOptions>>()
                .Value;

            var configurationOptions = ConfigurationOptions.Parse(options.ConnectionString);

            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ClientName = "RateLimiter";

            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        services.TryAddSingleton<IRateLimitKeyGenerator, DefaultRateLimitKeyGenerator>();
        services.TryAddSingleton<IFixedWindowStore, RedisFixedWindowStore>();
        services.TryAddSingleton<ISlidingWindowStore, RedisSlidingWindowStore>();

        return services;
    }
}