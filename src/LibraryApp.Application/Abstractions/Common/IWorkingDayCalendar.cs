namespace LibraryApp.Application.Abstractions.Common;

/// <summary>
/// Working days of the library (BR-BOR-5, BQ-G4-05): Monday to Friday except the configured Armenian public holidays.
/// One calendar for every module: overdue points and pickup deadlines (Borrowing), reminders (Notifications).
/// Dates are local Asia/Yerevan dates: convert with <see cref="IClock.ToLocalDate"/> first.
/// </summary>
public interface IWorkingDayCalendar
{
    bool IsWorkingDay(DateOnly date);

    /// <summary>The date <paramref name="days"/> working days after <paramref name="from"/>; 0 returns <paramref name="from"/>.</summary>
    DateOnly AddWorkingDays(DateOnly from, int days);

    /// <summary>Working days after <paramref name="fromExclusive"/> up to and including <paramref name="toInclusive"/>; 0 when the range is empty.</summary>
    int CountWorkingDays(DateOnly fromExclusive, DateOnly toInclusive);
}
