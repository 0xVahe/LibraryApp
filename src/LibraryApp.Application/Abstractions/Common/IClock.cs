namespace LibraryApp.Application.Abstractions.Common;

public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly LocalToday { get; }

    DateTime ToLocal(DateTime utc);

    DateOnly ToLocalDate(DateTime utc);

    DateTime StartOfLocalDayUtc(DateOnly localDate);

    DateTime EndOfLocalDayUtc(DateOnly localDate);
}
