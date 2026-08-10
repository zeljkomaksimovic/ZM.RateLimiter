using FluentAssertions;
using ZM.RateLimiter.Api.UnitTests.Builders;
using ZM.RateLimiter.Core.Options;

namespace ZM.RateLimiter.Api.UnitTests.Options
{
    public class RateLimitingOptionsValidatorTests
    {
        [Theory]
        [AutoMoqInlineData]
        public void Validate_WellFormedCatalogue_Succeeds(RateLimitingOptionsValidator sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build();

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Succeeded.Should().BeTrue();
        }

        [Theory]
        [AutoMoqInlineData]
        public void Validate_NoPolicies_Fails(RateLimitingOptionsValidator sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>(),
                apiKeys: new Dictionary<string, string>());

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("Policies");
        }

        [Theory]
        [AutoMoqInlineData(0)]
        [AutoMoqInlineData(-1)]
        public void Validate_NonPositiveLimit_Fails(long limit, RateLimitingOptionsValidator sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["free"] = RateLimitingOptionsBuilder.BuildPolicy(limit: limit)
                });

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("Limit");
        }

        [Theory]
        [AutoMoqInlineData]
        public void Validate_NonPositiveWindow_Fails(RateLimitingOptionsValidator sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["free"] = RateLimitingOptionsBuilder.BuildPolicy(window: TimeSpan.Zero)
                });

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("Window");
        }

        [Theory]
        [AutoMoqInlineData]
        public void Validate_DefaultPolicyThatDoesNotExist_Fails(RateLimitingOptionsValidator sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(defaultPolicy: "enterprise");

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("enterprise");
        }

        [Theory]
        [AutoMoqInlineData]
        public void Validate_ApiKeyMappedToMissingPolicy_FailsWithoutEchoingTheApiKey(
            RateLimitingOptionsValidator sut)
        {
            // Arrange
            const string apiKey = "super-secret-api-key";

            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["free"] = RateLimitingOptionsBuilder.BuildPolicy()
                },
                apiKeys: new Dictionary<string, string>
                {
                    [apiKey] = "policy-that-does-not-exist"
                });

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("policy-that-does-not-exist");
            result.FailureMessage.Should().NotContain(apiKey);
        }

        [Theory]
        [AutoMoqInlineData]
        public void Validate_CollectsEveryFailureRatherThanStoppingAtTheFirst(
            RateLimitingOptionsValidator sut)
        {
            // Arrange
            var options = RateLimitingOptionsBuilder.Build(
                defaultPolicy: "missing-default",
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["broken"] = RateLimitingOptionsBuilder.BuildPolicy(limit: 0, window: TimeSpan.Zero)
                },
                apiKeys: new Dictionary<string, string>
                {
                    ["key"] = "also-missing"
                });

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Failed.Should().BeTrue();
            result.Failures.Should().HaveCount(4);
        }
    }
}
