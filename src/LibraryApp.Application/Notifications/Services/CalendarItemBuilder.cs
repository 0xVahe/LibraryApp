using System.Globalization;
using System.Text;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// Builds the calendar item (iCalendar, RFC 5545, a <c>.ics</c> file) for the due date of a loan (NOT-07): an all-day event on
/// the due date that Outlook adds to the calendar. The same loan always gets the same <c>UID</c>, and <c>SEQUENCE</c> is the
/// number of renewals, so the item of a renewal updates the event of the issue instead of adding a second one.
/// </summary>
public static class CalendarItemBuilder
{
    private const int MaxLineOctets = 75;   // RFC 5545 3.1: longer lines are folded

    /// <param name="loanId">Decides the UID of the event.</param>
    /// <param name="bookTitle">Shown in the title of the event.</param>
    /// <param name="dueLocalDate">The due date in the library time zone (Asia/Yerevan); the event is all-day.</param>
    /// <param name="sequence">0 when the loan is issued, the renewal count after a renewal.</param>
    /// <param name="nowUtc">When the item is made (DTSTAMP), from IClock.</param>
    public static string Build(Guid loanId, string bookTitle, DateOnly dueLocalDate, int sequence, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookTitle);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The time must be UTC.", nameof(nowUtc));

        var builder = new StringBuilder();
        void Line(string line) => builder.Append(Fold(line)).Append("\r\n");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Ameriabank//Corporate Library//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line($"UID:loan-{loanId:D}@library.ameriabank");
        Line($"DTSTAMP:{nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}");
        Line($"SEQUENCE:{sequence.ToString(CultureInfo.InvariantCulture)}");
        Line($"DTSTART;VALUE=DATE:{dueLocalDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
        Line($"DTEND;VALUE=DATE:{dueLocalDate.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");   // the end of an all-day event is exclusive
        Line($"SUMMARY:{Escape($"Return \"{bookTitle}\" to the library")}");
        Line($"DESCRIPTION:{Escape("Return the book to the Ameriabank corporate library by this date.")}");
        Line("TRANSP:TRANSPARENT");
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return builder.ToString();
    }

    /// <summary>RFC 5545 3.3.11: backslash, semicolon, comma and line breaks are escaped in text values.</summary>
    private static string Escape(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// RFC 5545 3.1: a line is at most 75 octets (UTF-8); a longer one continues on the next line that starts with one space.
    /// A character is never split, so a title in Armenian stays valid.
    /// </summary>
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= MaxLineOctets)
            return line;

        var folded = new StringBuilder();
        var octets = 0;
        foreach (var element in line.EnumerateRunes())
        {
            var size = element.Utf8SequenceLength;
            if (octets + size > MaxLineOctets)
            {
                folded.Append("\r\n ");
                octets = 1;   // the leading space counts
            }

            folded.Append(element.ToString());
            octets += size;
        }

        return folded.ToString();
    }
}
