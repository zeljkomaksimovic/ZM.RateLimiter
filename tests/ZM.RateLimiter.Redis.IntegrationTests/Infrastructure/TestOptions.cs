using Microsoft.Extensions.Options;

namespace ZM.RateLimiter.Redis.IntegrationTests.Infrastructure;

internal static class TestOptions
{
    public static IOptions<TOptions> Create<TOptions>(TOptions value)
        where TOptions : class => Microsoft.Extensions.Options.Options.Create(value);
}
