using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ZM.RateLimiter.Api.IntegrationTests.Infrastructure;

internal sealed class RateLimiterApiFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public RateLimiterApiFactory(IReadOnlyDictionary<string, string?> settings, DateTimeOffset startTime)
    {
        _settings = settings;

        TimeProvider = new FakeTimeProvider(startTime);
    }

    public FakeTimeProvider TimeProvider { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps appsettings.Development.json out of the picture.
        builder.UseEnvironment("Testing");

        // Layered last, so these win over the shipped appsettings.json.
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);
        });
    }
}
