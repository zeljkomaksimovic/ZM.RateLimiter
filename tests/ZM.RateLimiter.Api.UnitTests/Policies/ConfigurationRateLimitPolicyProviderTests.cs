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
        public async Task GetPolicyAsync_MappedClientKey_ReturnsTheMappedPolicy(
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
                clientPolicies: new Dictionary<string, string>
                {
                    ["demo-pro-client"] = "pro"
                });

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("demo-pro-client", CancellationToken.None);

            // Assert
            policy.Should().NotBeNull();
            policy!.Name.Should().Be("pro");
            policy.Algorithm.Should().Be(RateLimitingAlgorithmType.SlidingWindow);
            policy.Limit.Should().Be(1000);
            policy.Window.Should().Be(TimeSpan.FromMinutes(5));
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task GetPolicyAsync_UnmappedClientKeyWithoutDefaultPolicy_ReturnsNull(
            [Frozen] Mock<IOptionsMonitor<RateLimitingOptions>> optionsMonitorMock,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                defaultPolicy: null,
                clientPolicies: new Dictionary<string, string>());

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
        public async Task GetPolicyAsync_ClientKeyMappedToMissingPolicy_ReturnsNull(
            [Frozen] Mock<IOptionsMonitor<RateLimitingOptions>> optionsMonitorMock,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["free"] = RateLimitingOptionsBuilder.BuildPolicy()
                },
                clientPolicies: new Dictionary<string, string>
                {
                    ["orphaned-client"] = "policy-that-does-not-exist"
                });

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("orphaned-client", CancellationToken.None);

            // Assert
            policy.Should().BeNull();
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task GetPolicyAsync_ClientKeyLookupIsCaseSensitive(
            [Frozen] Mock<IOptionsMonitor<RateLimitingOptions>> optionsMonitorMock,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                clientPolicies: new Dictionary<string, string>
                {
                    ["demo-free-client"] = "free"
                });

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("DEMO-FREE-CLIENT", CancellationToken.None);

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
                clientPolicies: new Dictionary<string, string>
                {
                    ["demo-free-client"] = "FREE"
                });

            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(options);

            // Act
            var policy = await sut.GetPolicyAsync("demo-free-client", CancellationToken.None);

            // Assert
            policy.Should().NotBeNull();
            policy!.Limit.Should().Be(42);
        }

        [Theory]
        [AutoMoqInlineData("")]
        [AutoMoqInlineData("   ")]
        public async Task GetPolicyAsync_BlankClientKey_Throws(
            string clientKey,
            ConfigurationRateLimitPolicyProvider sut)
        {
            // Act
            var act = async () => await sut.GetPolicyAsync(clientKey, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>();
        }
    }
}
