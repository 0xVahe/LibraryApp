using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LibraryApp.Infrastructure.Persistence.Converters;

/// <summary>
/// Refuses to write a DateTime that is not UTC (on save and as a query parameter or constant) and marks every value
/// read from the database as UTC. Registered once for DateTime; EF Core applies it to DateTime? as well
/// (null never reaches the converter).
/// <para>
/// A value with Kind = Unspecified is refused too: accepting it would silently store a local wall-clock time as if it
/// were UTC (four hours off for Yerevan).
/// </para>
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => UtcDateTime.RequireUtc(v),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public static class UtcDateTime
{
    public static DateTime RequireUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new InvalidOperationException(
                $"Only UTC DateTime values may be written to the database (got Kind = {value.Kind}). " +
                "Take the moment from IClock.UtcNow, or a library-day boundary from IClock.StartOfLocalDayUtc / " +
                "EndOfLocalDayUtc. DateTime.SpecifyKind does not convert anything, it only changes the label: use it " +
                "only for a value that is already UTC but lost its Kind (for example read from an external source).");
}
