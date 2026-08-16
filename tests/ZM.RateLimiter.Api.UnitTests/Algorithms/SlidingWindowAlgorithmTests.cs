using AutoFixture;
using AutoFixture.Xunit2;
using FluentAssertions;
using Moq;
using ZM.RateLimiter.Api.UnitTests.Builders;
using ZM.RateLimiter.Api.UnitTests.Fakes;
using ZM.RateLimiter.Core.Abstractions;
using ZM.RateLimiter.Core.Algorithms;
using ZM.RateLimiter.Core.Enums;
using ZM.RateLimiter.Core.Models;
using ZM.RateLimiter.Core.Stores;

namespace ZM.RateLimiter.Api.UnitTests.Algorithms
{
    public class SlidingWindowAlgorithmTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        [Theory]
        [AutoMoqInlineData]
        public void Type_IsSlidingWindow(SlidingWindowAlgorithm sut)
        {
            // Assert
            sut.Type.Should().Be(RateLimitingAlgorithmType.SlidingWindow);
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_WhenTheEntryWasAdded_ReturnsAllowed(
            [Frozen] Mock<ISlidingWindowStore> storeMock,
            [Frozen(Matching.DirectBaseType)] FixedTimeProvider timeProvider,
            SlidingWindowAlgorithm sut)
        {
            // Arrange
            timeProvider.UtcNow = Now;

            var policy = RateLimitPolicyBuilder.Build(
                algorithm: RateLimitingAlgorithmType.SlidingWindow,
                limit: 5,
                window: TimeSpan.FromMinutes(1));

            var request = new RateLimitRequest("partition", "orders");

            storeMock
                .Setup(x => x.ConsumeAsync(
                    request.CompositeKey,
                    Now,
                    policy.Window,
                    policy.Limit,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SlidingWindowStoreResult(true, 2, Now.AddSeconds(-30)));

            // Act
            var result = await sut.ConsumeAsync(request, policy, CancellationToken.None);

            // Assert
            result.IsAllowed.Should().BeTrue();
            result.Limit.Should().Be(5);
            result.Remaining.Should().Be(3);
            result.RetryAfter.Should().BeNull();

            storeMock.Verify(
                x => x.ConsumeAsync(
                    "partition:orders",
                    Now,
                    policy.Window,
                    policy.Limit,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_WhenRejected_RetryAfterIsWhenTheOldestEntryExpires(
            [Frozen] Mock<ISlidingWindowStore> storeMock,
            [Frozen(Matching.DirectBaseType)] FixedTimeProvider timeProvider,
            SlidingWindowAlgorithm sut)
        {
            // Arrange
            timeProvider.UtcNow = Now;

            var policy = RateLimitPolicyBuilder.Build(
                algorithm: RateLimitingAlgorithmType.SlidingWindow,
                limit: 5,
                window: TimeSpan.FromMinutes(1));

            var request = new RateLimitRequest("partition");

            storeMock
                .Setup(x => x.ConsumeAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SlidingWindowStoreResult(false, 5, Now.AddSeconds(-40)));

            // Act
            var result = await sut.ConsumeAsync(request, policy, CancellationToken.None);

            // Assert
            result.IsAllowed.Should().BeFalse();
            result.Remaining.Should().Be(0);

            result.RetryAfter.Should().Be(TimeSpan.FromSeconds(20));
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_WhenRejectedAndTheWindowIsEmpty_RetryAfterIsTheFullWindow(
            [Frozen] Mock<ISlidingWindowStore> storeMock,
            [Frozen(Matching.DirectBaseType)] FixedTimeProvider timeProvider,
            SlidingWindowAlgorithm sut)
        {
            // Arrange
            timeProvider.UtcNow = Now;

            var policy = RateLimitPolicyBuilder.Build(
                algorithm: RateLimitingAlgorithmType.SlidingWindow,
                limit: 5,
                window: TimeSpan.FromMinutes(1));

            var request = new RateLimitRequest("partition");

            storeMock
                .Setup(x => x.ConsumeAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SlidingWindowStoreResult(false, 5, null));

            // Act
            var result = await sut.ConsumeAsync(request, policy, CancellationToken.None);

            // Assert
            result.IsAllowed.Should().BeFalse();
            result.RetryAfter.Should().Be(policy.Window);
        }

        [Theory]
        [AutoMoqInlineData]
        public async Task ConsumeAsync_WhenTheOldestEntryHasAlreadyExpired_RetryAfterIsNeverNegative(
            [Frozen] Mock<ISlidingWindowStore> storeMock,
            [Frozen(Matching.DirectBaseType)] FixedTimeProvider timeProvider,
            SlidingWindowAlgorithm sut)
        {
            // Arrange
            timeProvider.UtcNow = Now;

            var policy = RateLimitPolicyBuilder.Build(
                algorithm: RateLimitingAlgorithmType.SlidingWindow,
                limit: 5,
                window: TimeSpan.FromMinutes(1));

            var request = new RateLimitRequest("partition");

            storeMock
                .Setup(x => x.ConsumeAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SlidingWindowStoreResult(false, 5, Now.AddMinutes(-10)));

            // Act
            var result = await sut.ConsumeAsync(request, policy, CancellationToken.None);

            // Assert
            result.RetryAfter.Should().Be(TimeSpan.Zero);
        }
    }
}
