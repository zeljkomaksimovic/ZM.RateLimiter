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
                clientPolicies: new Dictionary<string, string>());

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
        public void Validate_ClientKeyMappedToMissingPolicy_FailsWithoutEchoingTheClientKey(
            RateLimitingOptionsValidator sut)
        {
            // Arrange
            const string clientKey = "super-secret-client-key";

            var options = RateLimitingOptionsBuilder.Build(
                policies: new Dictionary<string, RateLimitPolicyOptions>
                {
                    ["free"] = RateLimitingOptionsBuilder.BuildPolicy()
                },
                clientPolicies: new Dictionary<string, string>
                {
                    [clientKey] = "policy-that-does-not-exist"
                });

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("policy-that-does-not-exist");
            result.FailureMessage.Should().NotContain(clientKey);
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
                clientPolicies: new Dictionary<string, string>
                {
                    ["client"] = "also-missing"
                });

            // Act
            var result = sut.Validate(null, options);

            // Assert
            result.Failed.Should().BeTrue();
            result.Failures.Should().HaveCount(4);
        }
    }
}
