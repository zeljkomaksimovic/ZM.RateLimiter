using Carter;
using ZM.RateLimiter.Core.DependencyInjection;
using ZM.RateLimiter.Redis.DependencyInjection;

namespace ZM.RateLimiter.Api.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static void RegisterServices(this IServiceCollection services, IConfiguration configuration)
        {
            RegisterCarter(services);
            RegisterRateLimiterCore(services, configuration);
            RegisterRedisRateLimiter(services, configuration);
        }

        private static void RegisterCarter(IServiceCollection services)
        {
            services.AddCarter();
        }

        private static void RegisterRateLimiterCore(IServiceCollection services, IConfiguration configuration)
        {
            services.AddRateLimiterCore(configuration);
        }

        private static void RegisterRedisRateLimiter(IServiceCollection services, IConfiguration configuration)
        {
            services.AddRedisRateLimiter(configuration);
        }
    }
}
