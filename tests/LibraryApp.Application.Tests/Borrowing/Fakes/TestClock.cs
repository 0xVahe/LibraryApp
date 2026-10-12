using System;
using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Application.Tests.Fakes;

public sealed class TestClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow.Kind == DateTimeKind.Utc
        ? utcNow
        : throw new ArgumentException("UtcNow must be UTC DateTimeKind.", nameof(utcNow));

    public DateOnly LocalToday => DateOnly.FromDateTime(UtcNow);

    public DateTime ToLocal(DateTime utc) => utc;

    public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(utc);

    public DateTime StartOfLocalDayUtc(DateOnly localDate) => localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public DateTime EndOfLocalDayUtc(DateOnly localDate) => StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);
}
