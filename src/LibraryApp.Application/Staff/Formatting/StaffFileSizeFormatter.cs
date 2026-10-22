using System.Globalization;

namespace LibraryApp.Application.Staff.Formatting;

/// <summary>Displays decimal file sizes: 1 KB = 1000 bytes and 1 MB = 1000000 bytes.</summary>
public static class StaffFileSizeFormatter
{
    public static string Format(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        var (value, unit) = bytes switch
        {
            >= 1_000_000 => (bytes / 1_000_000m, "MB"),
            >= 1_000 => (bytes / 1_000m, "KB"),
            _ => ((decimal)bytes, "bytes")
        };
        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + unit;
    }
}
