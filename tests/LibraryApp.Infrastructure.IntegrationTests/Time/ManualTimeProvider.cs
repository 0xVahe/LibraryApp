namespace LibraryApp.Infrastructure.IntegrationTests.Time;

/// <summary>A clock the test moves by hand; the real SystemClock on top of it gives Asia/Yerevan dates.</summary>
public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;

    public void Advance(TimeSpan by) => _utcNow += by;

    /// <summary>The UTC moment of an Asia/Yerevan wall time (UTC+4, no daylight saving).</summary>
    public static DateTimeOffset Yerevan(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(4));
}
