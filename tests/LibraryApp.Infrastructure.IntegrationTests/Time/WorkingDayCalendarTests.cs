using LibraryApp.Infrastructure.Time;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Time;

public class WorkingDayCalendarTests
{
    // Friday 9, Saturday 10, Sunday 11, Monday 12, Tuesday 13 October 2026.
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Tuesday = new(2026, 10, 13);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static WorkingDayCalendar Calendar(params DateOnly[] holidays) =>
        new(Options.Create(new HolidayOptions { Holidays = [.. holidays] }));

    [Fact]
    public void Weekends_and_holidays_are_not_working_days()
    {
        var calendar = Calendar(Monday);

        Assert.True(calendar.IsWorkingDay(Friday));
        Assert.False(calendar.IsWorkingDay(Friday.AddDays(1)));
        Assert.False(calendar.IsWorkingDay(Friday.AddDays(2)));
        Assert.False(calendar.IsWorkingDay(Monday));
        Assert.True(calendar.IsWorkingDay(Tuesday));
    }

    [Fact]
    public void A_weekend_in_the_middle_is_not_counted()
    {
        Assert.Equal(2, Calendar().CountWorkingDays(Friday, Tuesday));
    }

    [Fact]
    public void A_Monday_holiday_is_not_counted()
    {
        Assert.Equal(1, Calendar(Monday).CountWorkingDays(Friday, Tuesday));
    }

    [Fact]
    public void Returning_on_the_due_date_or_earlier_counts_no_working_day()
    {
        var calendar = Calendar();

        Assert.Equal(0, calendar.CountWorkingDays(Tuesday, Tuesday));
        Assert.Equal(0, calendar.CountWorkingDays(Tuesday, Monday));
    }

    [Fact]
    public void Adding_working_days_skips_the_weekend_and_holidays()
    {
        Assert.Equal(Monday, Calendar().AddWorkingDays(Friday, 1));
        Assert.Equal(Tuesday, Calendar(Monday).AddWorkingDays(Friday, 1));
        Assert.Equal(new DateOnly(2026, 10, 15), Calendar(Monday).AddWorkingDays(Friday, 3));
    }

    [Fact]
    public void Adding_zero_working_days_keeps_the_date_and_a_negative_number_is_refused()
    {
        var calendar = Calendar();

        Assert.Equal(Friday, calendar.AddWorkingDays(Friday, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.AddWorkingDays(Friday, -1));
    }

    [Theory]
    [InlineData("2026-01-01", true)]
    [InlineData("2025-12-31", false)]
    public void The_holiday_list_must_cover_the_current_library_year(string holiday, bool valid)
    {
        var clock = new SystemClock(
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero)),
            Options.Create(new TimeZoneOptions()));
        var options = new HolidayOptions { Holidays = [DateOnly.Parse(holiday)] };

        ValidateOptionsResult result = new HolidayOptionsValidator(clock).Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
    }
}
