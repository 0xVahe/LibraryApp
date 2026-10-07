using LibraryApp.Infrastructure.Time;

namespace LibraryApp.Domain.Tests.Borrowing;

public class WorkingDayCalendarTests
{
    [Fact]
    public void WeekendInTheMiddle_ShouldNotBeCounted()
    {
        var calendar = new WorkingDayCalendar(
            new WorkingDayOptions());

        var from = new DateOnly(2026, 10, 9);
        var to = new DateOnly(2026, 10, 13);

        var result = calendar.GetWorkingDaysBetween(from, to);

        Assert.Equal(2, result);
    }

    [Fact]
    public void MondayHoliday_ShouldNotBeCounted()
    {
        var holiday = new DateOnly(2026, 10, 12);

        var options = new WorkingDayOptions
        {
            Holidays = [holiday]
        };

        var calendar = new WorkingDayCalendar(options);

        var from = new DateOnly(2026, 10, 9);
        var to = new DateOnly(2026, 10, 13);

        var result = calendar.GetWorkingDaysBetween(from, to);

        Assert.Equal(1, result);
    }

    [Fact]
    public void ReturnOnDueDate_ShouldHaveZeroLateWorkingDays()
    {
        var calendar = new WorkingDayCalendar(
            new WorkingDayOptions());

        var dueDate = new DateOnly(2026, 10, 12);
        var returnDate = new DateOnly(2026, 10, 12);

        var result = calendar.GetWorkingDaysBetween(
            dueDate,
            returnDate);

        Assert.Equal(0, result);
    }

    [Fact]
    public void AddWorkingDays_ShouldSkipWeekend()
    {
        var calendar = new WorkingDayCalendar(
            new WorkingDayOptions());

        var friday = new DateOnly(2026, 10, 9);

        var result = calendar.AddWorkingDays(friday, 1);

        Assert.Equal(
            new DateOnly(2026, 10, 12),
            result);
    }

    [Fact]
    public void AddWorkingDays_ShouldSkipHoliday()
    {
        var holiday = new DateOnly(2026, 10, 12);

        var options = new WorkingDayOptions
        {
            Holidays = [holiday]
        };

        var calendar = new WorkingDayCalendar(options);

        var friday = new DateOnly(2026, 10, 9);

        var result = calendar.AddWorkingDays(friday, 1);

        Assert.Equal(
            new DateOnly(2026, 10, 13),
            result);
    }
}