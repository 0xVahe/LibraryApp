using LibraryApp.Application.Borrowing.Ports;

namespace LibraryApp.Infrastructure.Time;

public class WorkingDayCalendar : IWorkingDayCalendar
{
    private readonly HashSet<DateOnly> _holidays;

    public WorkingDayCalendar(WorkingDayOptions options)
    {
        _holidays = options.Holidays.ToHashSet();
    }

    public int GetWorkingDaysBetween(DateOnly from, DateOnly to)
    {
        if (from >= to)
        {
            return 0;
        }

        int workingDays = 0;
        var current = from.AddDays(1);

        while (current <= to)
        {
            if (IsWorkingDay(current))
            {
                workingDays++;
            }

            current = current.AddDays(1);
        }

        return workingDays;
    }

    public DateOnly AddWorkingDays(DateOnly date, int workingDays)
    {
        if (workingDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workingDays));
        }

        var current = date;
        int addedDays = 0;

        while (addedDays < workingDays)
        {
            current = current.AddDays(1);

            if (IsWorkingDay(current))
            {
                addedDays++;
            }
        }

        return current;
    }

    private bool IsWorkingDay(DateOnly date)
    {
        if (date.DayOfWeek == DayOfWeek.Saturday ||
            date.DayOfWeek == DayOfWeek.Sunday)
        {
            return false;
        }

        return !_holidays.Contains(date);
    }
}