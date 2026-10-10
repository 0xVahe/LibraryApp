using System.Collections.Frozen;
using LibraryApp.Application.Abstractions.Common;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Time;

/// <summary><see cref="IWorkingDayCalendar"/> over <see cref="HolidayOptions"/>. Immutable, register as a singleton.</summary>
public sealed class WorkingDayCalendar(IOptions<HolidayOptions> options) : IWorkingDayCalendar
{
    private readonly FrozenSet<DateOnly> _holidays = options.Value.Holidays.ToFrozenSet();

    public bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_holidays.Contains(date);

    public DateOnly AddWorkingDays(DateOnly from, int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);

        DateOnly current = from;
        for (int added = 0; added < days;)
        {
            current = current.AddDays(1);
            if (IsWorkingDay(current))
                added++;
        }

        return current;
    }

    public int CountWorkingDays(DateOnly fromExclusive, DateOnly toInclusive)
    {
        int count = 0;
        for (DateOnly current = fromExclusive.AddDays(1); current <= toInclusive; current = current.AddDays(1))
        {
            if (IsWorkingDay(current))
                count++;
        }

        return count;
    }
}
