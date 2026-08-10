using AutoFixture.Xunit2;
using FluentAssertions;
using Moq;
using ZM.RateLimiter.Api.UnitTests.Builders;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Models;
using ZM.RateLimiter.Core.Services;

namespace ZM.RateLimiter.Api.UnitTests.Services
{
    public class RateLimiterServiceTests
    {
        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_UnknownApiKey_ReturnsNullWithoutResolvingAlgorithm(
            [Frozen] Mock<IRateLimitPolicyProvider> policyProviderMock,
            [Frozen] Mock<IRateLimitingAlgorithmFactory> algorithmFactoryMock,
            RateLimiterService sut)
        {
            // Arrange
            policyProviderMock
                .Setup(x => x.GetPolicyAsync("unknown-key", It.IsAny<CancellationToken>()))
                .ReturnsAsync((RateLimitPolicy?)null);

            // Act
            var outcome = await sut.ConsumeAsync("unknown-key", null, CancellationToken.None);

            // Assert
            outcome.Should().BeNull();

            algorithmFactoryMock.Verify(
                x => x.Resolve(It.IsAny<RateLimitingAlgorithmType>()),
                Times.Never);
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_KnownApiKey_ResolvesTheAlgorithmNamedByThePolicy(
            [Frozen] Mock<IRateLimitPolicyProvider> policyProviderMock,
            [Frozen] Mock<IRateLimitingAlgorithmFactory> algorithmFactoryMock,
            Mock<IRateLimiterAlgorithm> algorithmMock,
            RateLimiterService sut)
        {
            // Arrange
            var policy = RateLimitPolicyBuilder.Build(
                name: "pro",
                algorithm: RateLimitingAlgorithmType.SlidingWindow,
                limit: 100);

            var expected = RateLimitResult.Allowed(policy.Limit, policy.Limit - 1);

            policyProviderMock
                .Setup(x => x.GetPolicyAsync("demo-pro-key", It.IsAny<CancellationToken>()))
                .ReturnsAsync(policy);

            algorithmFactoryMock
                .Setup(x => x.Resolve(RateLimitingAlgorithmType.SlidingWindow))
                .Returns(algorithmMock.Object);

            algorithmMock
                .Setup(x => x.ConsumeAsync(
                    It.IsAny<RateLimitRequest>(),
                    policy,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            // Act
            var outcome = await sut.ConsumeAsync("demo-pro-key", "orders", CancellationToken.None);

            // Assert
            outcome.Should().NotBeNull();
            outcome!.Policy.Should().Be(policy);
            outcome.Result.Should().Be(expected);

            algorithmFactoryMock.Verify(
                x => x.Resolve(RateLimitingAlgorithmType.SlidingWindow),
                Times.Once);

            algorithmMock.Verify(
                x => x.ConsumeAsync(
                    It.IsAny<RateLimitRequest>(),
                    policy,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_KnownApiKey_PartitionsOnADigestRatherThanTheRawApiKey(
            [Frozen] Mock<IRateLimitPolicyProvider> policyProviderMock,
            [Frozen] Mock<IRateLimitingAlgorithmFactory> algorithmFactoryMock,
            Mock<IRateLimiterAlgorithm> algorithmMock,
            RateLimiterService sut)
        {
            // Arrange
            const string apiKey = "super-secret-api-key";

            var policy = RateLimitPolicyBuilder.Build();
            var captured = default(RateLimitRequest);

            policyProviderMock
                .Setup(x => x.GetPolicyAsync(apiKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(policy);

            algorithmFactoryMock
                .Setup(x => x.Resolve(It.IsAny<RateLimitingAlgorithmType>()))
                .Returns(algorithmMock.Object);

            algorithmMock
                .Setup(x => x.ConsumeAsync(
                    It.IsAny<RateLimitRequest>(),
                    It.IsAny<RateLimitPolicy>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RateLimitRequest, RateLimitPolicy, CancellationToken>((request, _, _) => captured = request)
                .ReturnsAsync(RateLimitResult.Allowed(policy.Limit, policy.Limit - 1));

            // Act
            await sut.ConsumeAsync(apiKey, "orders", CancellationToken.None);

            // Assert
            captured.Should().NotBeNull();
            captured!.Key.Should().NotContain(apiKey);
            captured.Key.Should().MatchRegex("^[0-9a-f]{32}$");
            captured.Resource.Should().Be("orders");
            captured.CompositeKey.Should().Be($"{captured.Key}:orders");
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_SameApiKey_ProducesAStablePartitionAcrossCalls(
            [Frozen] Mock<IRateLimitPolicyProvider> policyProviderMock,
            [Frozen] Mock<IRateLimitingAlgorithmFactory> algorithmFactoryMock,
            Mock<IRateLimiterAlgorithm> algorithmMock,
            RateLimiterService sut)
        {
            // Arrange
            var policy = RateLimitPolicyBuilder.Build();
            var captured = new List<string>();

            policyProviderMock
                .Setup(x => x.GetPolicyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(policy);

            algorithmFactoryMock
                .Setup(x => x.Resolve(It.IsAny<RateLimitingAlgorithmType>()))
                .Returns(algorithmMock.Object);

            algorithmMock
                .Setup(x => x.ConsumeAsync(
                    It.IsAny<RateLimitRequest>(),
                    It.IsAny<RateLimitPolicy>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RateLimitRequest, RateLimitPolicy, CancellationToken>((request, _, _) => captured.Add(request.Key))
                .ReturnsAsync(RateLimitResult.Allowed(policy.Limit, policy.Limit - 1));

            // Act
            await sut.ConsumeAsync("key-a", null, CancellationToken.None);
            await sut.ConsumeAsync("key-a", null, CancellationToken.None);
            await sut.ConsumeAsync("key-b", null, CancellationToken.None);

            // Assert
            captured[0].Should().Be(captured[1]);
            captured[2].Should().NotBe(captured[0]);
        }

        [Theory]
        [AutoMoqInlineData("")]
        [AutoMoqInlineData("   ")]
        public async Task ConsumeAsync_BlankApiKey_Throws(
            string apiKey,
            RateLimiterService sut)
        {
            // Act
            var act = async () => await sut.ConsumeAsync(apiKey, null, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>();
        }
    }
}
