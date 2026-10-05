using LibraryApp.Infrastructure.Time;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Time;

public class SystemClockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SystemClock ClockAt(DateTimeOffset utcNow, string zone = "Asia/Yerevan") =>
        new(new FixedTimeProvider(utcNow), Options.Create(new TimeZoneOptions { TimeZoneId = zone }));

    private static SystemClock AnyClock() => ClockAt(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void UtcNow_comes_from_the_time_provider_as_UTC()
    {
        var clock = ClockAt(new DateTimeOffset(2026, 10, 12, 20, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2026, 10, 12, 20, 30, 0, DateTimeKind.Utc), clock.UtcNow);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }

    [Fact]
    public void LocalToday_is_counted_in_Yerevan_not_in_UTC()
    {
        // 20:30 UTC on 12 October is already 00:30 on 13 October in Yerevan (UTC+4).
        var clock = ClockAt(new DateTimeOffset(2026, 10, 12, 20, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 13), clock.LocalToday);
    }

    [Fact]
    public void LocalToday_is_still_the_same_day_one_tick_before_Yerevan_midnight()
    {
        var clock = ClockAt(new DateTimeOffset(2026, 10, 12, 19, 59, 59, TimeSpan.Zero).AddTicks(9_999_999));

        Assert.Equal(new DateOnly(2026, 10, 12), clock.LocalToday);
    }

    [Fact]
    public void StartOfLocalDayUtc_is_four_hours_before_UTC_midnight()
    {
        var start = AnyClock().StartOfLocalDayUtc(new DateOnly(2026, 10, 13));

        Assert.Equal(new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public void EndOfLocalDayUtc_is_one_tick_before_the_next_local_day()
    {
        var clock = AnyClock();

        var end = clock.EndOfLocalDayUtc(new DateOnly(2026, 10, 13));

        Assert.Equal(clock.StartOfLocalDayUtc(new DateOnly(2026, 10, 14)).AddTicks(-1), end);
        Assert.Equal(DateTimeKind.Utc, end.Kind);
    }

    [Fact]
    public void ToLocal_returns_Yerevan_wall_time_that_cannot_be_saved_as_UTC()
    {
        var local = AnyClock().ToLocal(new DateTime(2026, 10, 12, 20, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 13, 0, 30, 0), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
    }

    [Fact]
    public void ToLocalDate_crosses_the_date_line_at_Yerevan_midnight()
    {
        var clock = AnyClock();

        Assert.Equal(new DateOnly(2026, 10, 12), clock.ToLocalDate(new DateTime(2026, 10, 12, 19, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 13), clock.ToLocalDate(new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ToLocal_refuses_a_non_UTC_value(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(() => AnyClock().ToLocal(new DateTime(2026, 10, 12, 12, 0, 0, kind)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void An_unknown_time_zone_fails_at_start_with_a_clear_message(string zone)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ClockAt(DateTimeOffset.UnixEpoch, zone));

        Assert.Contains("TimeZoneId", ex.Message);
    }
}
