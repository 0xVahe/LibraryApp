namespace LibraryApp.Application.Abstractions.Common;

/// <summary>
/// The only source of "now" in application code (SD-03). Never call DateTime.Now / DateTime.UtcNow directly:
/// tests replace the clock, and days (overdue, working days) are counted in Asia/Yerevan (NFR-12).
/// </summary>
public interface IClock
{
    /// <summary>The current moment in UTC (what is written to the database).</summary>
    DateTime UtcNow { get; }

    /// <summary>Today's date in the library time zone (Asia/Yerevan).</summary>
    DateOnly LocalToday { get; }

    DateTime ToLocal(DateTime utc);

    DateOnly ToLocalDate(DateTime utc);

    /// <summary>00:00 of the local day, expressed in UTC.</summary>
    DateTime StartOfLocalDayUtc(DateOnly localDate);

    /// <summary>The last tick of the local day, expressed in UTC.</summary>
    DateTime EndOfLocalDayUtc(DateOnly localDate);
}
