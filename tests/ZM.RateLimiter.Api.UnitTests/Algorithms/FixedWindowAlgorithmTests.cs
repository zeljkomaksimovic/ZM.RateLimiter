using AutoFixture.Xunit2;
using FluentAssertions;
using Moq;
using ZM.RateLimiter.Api.UnitTests.Builders;
using ZM.RateLimiter.Api.UnitTests.Fakes;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Algorithms;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Models;

namespace ZM.RateLimiter.Api.UnitTests.Algorithms
{
    public class FixedWindowAlgorithmTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 30, TimeSpan.Zero);

        [Theory]
        [AutoMoqInlineData]
        public void Type_IsFixedWindow(FixedWindowAlgorithm sut)
        {
            // Assert
            sut.Type.Should().Be(RateLimitingAlgorithmType.FixedWindow);
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_UnderTheLimit_ReturnsAllowedWithRemaining(
            [Frozen] Mock<IFixedWindowStore> storeMock,
            [Frozen] Mock<IRateLimitKeyGenerator> keyGeneratorMock,
            [Frozen(Matching.DirectBaseType)] FixedTimeProvider timeProvider,
            FixedWindowAlgorithm sut)
        {
            // Arrange
            timeProvider.UtcNow = Now;

            var policy = RateLimitPolicyBuilder.Build(limit: 10, window: TimeSpan.FromMinutes(1));
            var request = new RateLimitRequest("partition", "orders");

            keyGeneratorMock
                .Setup(x => x.CreateWindowKey(request, Now, policy.Window))
                .Returns("window-key");

            storeMock
                .Setup(x => x.IncrementAsync("window-key", policy.Window, It.IsAny<CancellationToken>()))
                .ReturnsAsync(3);

            // Act
            var result = await sut.ConsumeAsync(request, policy, CancellationToken.None);

            // Assert
            result.IsAllowed.Should().BeTrue();
            result.Limit.Should().Be(10);
            result.Remaining.Should().Be(7);
            result.RetryAfter.Should().BeNull();

            keyGeneratorMock.Verify(
                x => x.CreateWindowKey(request, Now, policy.Window),
                Times.Once);

            storeMock.Verify(
                x => x.IncrementAsync("window-key", policy.Window, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_ExactlyAtTheLimit_IsStillAllowed(
            [Frozen] Mock<IFixedWindowStore> storeMock,
            [Frozen] Mock<IRateLimitKeyGenerator> keyGeneratorMock,
            [Frozen(Matching.DirectBaseType)] FixedTimeProvider timeProvider,
            FixedWindowAlgorithm sut)
        {
            // Arrange
            timeProvider.UtcNow = Now;

            var policy = RateLimitPolicyBuilder.Build(limit: 10, window: TimeSpan.FromMinutes(1));
            var request = new RateLimitRequest("partition");

            keyGeneratorMock
                .Setup(x => x.CreateWindowKey(It.IsAny<RateLimitRequest>(), It.IsAny<DateTimeOffset>(), It.IsAny<TimeSpan>()))
                .Returns("window-key");

            storeMock
                .Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(10);

            // Act
            var result = await sut.ConsumeAsync(request, policy, CancellationToken.None);

            // Assert
            result.IsAllowed.Should().BeTrue();
            result.Remaining.Should().Be(0);
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_OverTheLimit_ReturnsDeniedWithRetryAfterUntilTheWindowRolls(
            [Frozen] Mock<IFixedWindowStore> storeMock,
            [Frozen] Mock<IRateLimitKeyGenerator> keyGeneratorMock,
            [Frozen(Matching.DirectBaseType)] FixedTimeProvider timeProvider,
            FixedWindowAlgorithm sut)
        {
            // Arrange
            timeProvider.UtcNow = Now;

            var policy = RateLimitPolicyBuilder.Build(limit: 10, window: TimeSpan.FromMinutes(1));
            var request = new RateLimitRequest("partition");

            keyGeneratorMock
                .Setup(x => x.CreateWindowKey(It.IsAny<RateLimitRequest>(), It.IsAny<DateTimeOffset>(), It.IsAny<TimeSpan>()))
                .Returns("window-key");

            storeMock
                .Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(11);

            // Act
            var result = await sut.ConsumeAsync(request, policy, CancellationToken.None);

            // Assert
            result.IsAllowed.Should().BeFalse();
            result.Limit.Should().Be(10);
            result.Remaining.Should().Be(0);

            result.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
        }
    }
}
