using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Algorithms;
using ZM.RateLimiter.Core.Factories;
using ZM.RateLimiter.Core.Options;
using ZM.RateLimiter.Core.Policies;
using ZM.RateLimiter.Core.Services;

namespace ZM.RateLimiter.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRateLimiterCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRateLimiterAlgorithm, FixedWindowAlgorithm>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRateLimiterAlgorithm, SlidingWindowAlgorithm>());

        services.TryAddSingleton<IRateLimitingAlgorithmFactory, RateLimitingAlgorithmFactory>();
        services.TryAddSingleton<IRateLimitPolicyProvider, ConfigurationRateLimitPolicyProvider>();
        services.TryAddSingleton<IRateLimiter, RateLimiterService>();

        return services;
    }
}
