using AutoFixture.Xunit2;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ZM.RateLimiter.Api.UnitTests.Builders;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Options;
using ZM.RateLimiter.Core.Policies;

namespace ZM.RateLimiter.Api.UnitTests.Policies
{
    public class ConfigurationRateLimitPolicyProviderTests
    {
        [Theory]
        [AutoMoqInlineData]
        public async Task GetPolicyAsync_MappedApiKey_ReturnsTheMappedPolicy(
            [Frozen] Mock<IOptionsMonitor<RateLimitingOptions>> optionsMonitorMock,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["pro"] = RateLimitingOptionsBuilder.BuildPolicy(
                        RateLimitingAlgorithmType.SlidingWindow,
                        limit: 1000,
                        window: TimeSpan.FromMinutes(5))
                },
                apiKeys: new Dictionary<string, string>
                {
                    ["demo-pro-key"] = "pro"
                });

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("demo-pro-key", CancellationToken.None);

            // Assert
            policy.Should().NotBeNull();
            policy!.Name.Should().Be("pro");
            policy.Algorithm.Should().Be(RateLimitingAlgorithmType.SlidingWindow);
            policy.Limit.Should().Be(1000);
            policy.Window.Should().Be(TimeSpan.FromMinutes(5));
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task GetPolicyAsync_UnmappedApiKeyWithoutDefaultPolicy_ReturnsNull(
            [Frozen] Mock<IOptionsMonitor<RateLimitingOptions>> optionsMonitorMock,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                defaultPolicy: null,
                apiKeys: new Dictionary<string, string>());

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("never-seen-before", CancellationToken.None);

            // Assert
            policy.Should().BeNull();
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task GetPolicyAsync_ApiKeyMappedToMissingPolicy_ReturnsNull(
            [Frozen] Mock<IOptionsMonitor<RateLimitingOptions>> optionsMonitorMock,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["free"] = RateLimitingOptionsBuilder.BuildPolicy()
                },
                apiKeys: new Dictionary<string, string>
                {
                    ["orphaned-key"] = "policy-that-does-not-exist"
                });

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("orphaned-key", CancellationToken.None);

            // Assert
            policy.Should().BeNull();
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task GetPolicyAsync_ApiKeyLookupIsCaseSensitive(
            [Frozen] Mock<IOptionsMonitor<RateLimitingOptions>> optionsMonitorMock,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                apiKeys: new Dictionary<string, string>
                {
                    ["demo-free-key"] = "free"
                });

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("DEMO-FREE-KEY", CancellationToken.None);

            // Assert
            policy.Should().BeNull();
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task GetPolicyAsync_PolicyNameLookupIsCaseInsensitive(
            [Frozen] Mock<IOptionsMonitor<RateLimitingOptions>> optionsMonitorMock,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["free"] = RateLimitingOptionsBuilder.BuildPolicy(limit: 42)
                },
                apiKeys: new Dictionary<string, string>
                {
                    ["demo-free-key"] = "FREE"
                });

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("demo-free-key", CancellationToken.None);

            // Assert
            policy.Should().NotBeNull();
            policy!.Limit.Should().Be(42);
        }

        [Theory]
        [AutoMoqInlineData("")]
        [AutoMoqInlineData("   ")]
        public async Task GetPolicyAsync_BlankApiKey_Throws(
            string apiKey,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Act
            var act = async () => await sut.GetPolicyAsync(apiKey, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>();
        }
    }
}
