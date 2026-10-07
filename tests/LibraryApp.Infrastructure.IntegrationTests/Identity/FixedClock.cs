using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Infrastructure.IntegrationTests.Identity;

internal sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow => utcNow;
    public DateOnly LocalToday => DateOnly.FromDateTime(utcNow);
    public DateTime ToLocal(DateTime utc) => utc;
    public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(utc);
    public DateTime StartOfLocalDayUtc(DateOnly localDate) => localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    public DateTime EndOfLocalDayUtc(DateOnly localDate) => localDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
}