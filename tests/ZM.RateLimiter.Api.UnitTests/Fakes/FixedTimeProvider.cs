namespace ZM.RateLimiter.Api.UnitTests.Fakes
{
    public sealed class FixedTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
