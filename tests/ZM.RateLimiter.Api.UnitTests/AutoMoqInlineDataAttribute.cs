using AutoFixture;
using AutoFixture.AutoMoq;
using AutoFixture.Xunit2;

namespace ZM.RateLimiter.Api.UnitTests
{
    public class AutoMoqInlineDataAttribute : InlineAutoDataAttribute
    {
        public AutoMoqInlineDataAttribute(params object[] values) : base(new AutoMoqAttribute(), values)
        {

        }

        private class AutoMoqAttribute : AutoDataAttribute
        {
            public AutoMoqAttribute() : base(FixtureFactory)
            {

            }

            private static IFixture FixtureFactory()
            {
                var fixture = new Fixture();

                fixture.Customize(new AutoMoqCustomization { ConfigureMembers = true });
                fixture.Behaviors.OfType<ThrowingRecursionBehavior>().ToList()
                    .ForEach(b => fixture.Behaviors.Remove(b));
                fixture.Behaviors.Add(new OmitOnRecursionBehavior());

                fixture.Register(() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
                fixture.Register(() => TimeSpan.FromMinutes(1));

                return fixture;
            }
        }
    }
}
