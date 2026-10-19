using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

public sealed class FixedYerevanClock : IClock
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan");

    private DateTime _utcNow;

    public FixedYerevanClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow
    {
        get => _utcNow;
        set
        {
            if (value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    "The test clock requires UTC.",
                    nameof(value));
            }

            _utcNow = value;
        }
    }

    public DateOnly LocalToday => ToLocalDate(UtcNow);

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);

    public DateOnly ToLocalDate(DateTime utc) =>
        DateOnly.FromDateTime(ToLocal(utc));

    public DateTime StartOfLocalDayUtc(DateOnly localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(
            localDate.ToDateTime(
                TimeOnly.MinValue,
                DateTimeKind.Unspecified),
            Zone);

    public DateTime EndOfLocalDayUtc(DateOnly localDate) =>
        StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);
}