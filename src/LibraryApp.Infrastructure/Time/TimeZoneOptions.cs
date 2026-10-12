namespace LibraryApp.Infrastructure.Time;

/// <summary>Configuration section <c>Time</c>. The library counts days in Asia/Yerevan (NFR-12).</summary>
public sealed class TimeZoneOptions
{
    public const string SectionName = "Time";

    public string TimeZoneId { get; set; } = "Asia/Yerevan";
}
