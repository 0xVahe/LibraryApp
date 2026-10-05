using LibraryApp.Application.Abstractions.Common;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Time;

/// <summary>
/// <see cref="IClock"/> over <see cref="TimeProvider"/> (tests pass a fixed one) and the library time zone from
/// <see cref="TimeZoneOptions"/> (NFR-12). Register as a singleton.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;

    public SystemClock(TimeProvider time, IOptions<TimeZoneOptions> options)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);

        _time = time;
        _zone = FindZone(options.Value.TimeZoneId);
    }

    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public DateOnly LocalToday => ToLocalDate(UtcNow);

    /// <summary>The local wall-clock time (Kind = Unspecified: it is never written to the database).</summary>
    public DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(RequireUtc(utc), _zone), DateTimeKind.Unspecified);

    public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public DateTime StartOfLocalDayUtc(DateOnly localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _zone);

    public DateTime EndOfLocalDayUtc(DateOnly localDate) => StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);

    private static DateTime RequireUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException($"A UTC DateTime is expected (got Kind = {value.Kind}).", nameof(value));

    private static TimeZoneInfo FindZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException($"'{TimeZoneOptions.SectionName}:TimeZoneId' is empty.");

        // .NET maps IANA ids ("Asia/Yerevan") to Windows ids and back where ICU or tzdata is available.
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            return zone;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone))
            return zone;

        throw new InvalidOperationException(
            $"Time zone '{id}' ('{TimeZoneOptions.SectionName}:TimeZoneId') is not known on this machine. " +
            "On Linux images install tzdata.");
    }
}
