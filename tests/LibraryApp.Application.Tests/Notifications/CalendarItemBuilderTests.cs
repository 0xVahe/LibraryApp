using System.Text;
using LibraryApp.Application.Notifications.Services;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class CalendarItemBuilderTests
{
    private static readonly Guid LoanId = new("0a1b2c3d-0000-4000-8000-00000000abcd");
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static string[] Lines(string ics) => ics.Split("\r\n", StringSplitOptions.None);

    [Fact]
    public void Builds_an_all_day_event_on_the_due_date_with_the_loan_uid()
    {
        var ics = CalendarItemBuilder.Build(LoanId, "Clean Code", new DateOnly(2026, 11, 13), sequence: 0, Now);

        var lines = Lines(ics);
        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Contains("VERSION:2.0", lines);
        Assert.Contains("METHOD:PUBLISH", lines);
        Assert.Contains("BEGIN:VEVENT", lines);
        Assert.Contains("UID:loan-0a1b2c3d-0000-4000-8000-00000000abcd@library.ameriabank", lines);
        Assert.Contains("DTSTAMP:20261020T120000Z", lines);
        Assert.Contains("SEQUENCE:0", lines);
        Assert.Contains("DTSTART;VALUE=DATE:20261113", lines);
        Assert.Contains("DTEND;VALUE=DATE:20261114", lines);              // the end of an all-day event is the next day
        Assert.Contains("SUMMARY:Return \"Clean Code\" to the library", lines);
        Assert.Contains("END:VEVENT", lines);
        Assert.Equal("END:VCALENDAR", lines[^2]);
        Assert.Equal(string.Empty, lines[^1]);                              // every line, also the last, ends with CRLF
        Assert.DoesNotContain("\n", ics.Replace("\r\n", string.Empty));    // no bare LF
    }

    [Fact]
    public void The_end_of_the_event_crosses_the_end_of_a_month_and_a_year()
    {
        var ics = CalendarItemBuilder.Build(LoanId, "Book", new DateOnly(2026, 12, 31), sequence: 0, Now);

        Assert.Contains("DTSTART;VALUE=DATE:20261231", Lines(ics));
        Assert.Contains("DTEND;VALUE=DATE:20270101", Lines(ics));
    }

    [Fact]
    public void A_renewal_keeps_the_uid_and_raises_the_sequence_so_the_event_is_updated()
    {
        var issued = Lines(CalendarItemBuilder.Build(LoanId, "Clean Code", new DateOnly(2026, 11, 13), 0, Now));
        var renewed = Lines(CalendarItemBuilder.Build(LoanId, "Clean Code", new DateOnly(2026, 12, 4), 1, Now.AddDays(3)));

        Assert.Equal(issued.Single(l => l.StartsWith("UID:", StringComparison.Ordinal)), renewed.Single(l => l.StartsWith("UID:", StringComparison.Ordinal)));
        Assert.Contains("SEQUENCE:1", renewed);
        Assert.Contains("DTSTART;VALUE=DATE:20261204", renewed);
    }

    [Fact]
    public void Special_characters_of_the_title_are_escaped()
    {
        var ics = CalendarItemBuilder.Build(LoanId, "Design; Patterns, Vol 1\\2\nsecond line", new DateOnly(2026, 11, 13), 0, Now);

        Assert.Contains("SUMMARY:Return \"Design\\; Patterns\\, Vol 1\\\\2\\nsecond line\" to the library", Lines(ics));
    }

    [Fact]
    public void Lines_over_75_octets_are_folded_without_splitting_a_character()
    {
        var title = string.Concat(Enumerable.Repeat("Գիրք", 40));   // Armenian: two octets per letter

        var ics = CalendarItemBuilder.Build(LoanId, title, new DateOnly(2026, 11, 13), 0, Now);

        var lines = Lines(ics);
        Assert.All(lines, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"too long: {line}"));
        Assert.Contains(lines, line => line.StartsWith(' '));           // a continuation line starts with a space
        var unfolded = ics.Replace("\r\n ", string.Empty);
        Assert.Contains($"SUMMARY:Return \"{title}\" to the library", unfolded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_is_refused(string title) =>
        Assert.Throws<ArgumentException>(() => CalendarItemBuilder.Build(LoanId, title, new DateOnly(2026, 11, 13), 0, Now));

    [Fact]
    public void A_negative_sequence_and_a_non_utc_time_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarItemBuilder.Build(LoanId, "Book", new DateOnly(2026, 11, 13), -1, Now));
        Assert.Throws<ArgumentException>(() => CalendarItemBuilder.Build(
            LoanId, "Book", new DateOnly(2026, 11, 13), 0, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }
}
