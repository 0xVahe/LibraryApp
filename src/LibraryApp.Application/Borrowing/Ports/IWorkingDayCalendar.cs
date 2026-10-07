namespace LibraryApp.Application.Borrowing.Ports;

public interface IWorkingDayCalendar
{
    int GetWorkingDaysBetween(DateOnly from, DateOnly to);

    DateOnly AddWorkingDays(DateOnly date, int workingDays);
}