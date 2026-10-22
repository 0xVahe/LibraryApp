using System.Globalization;
using System.Text.RegularExpressions;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>Placeholder names used by the templates (NOT-02) and the date format of the emails.</summary>
public static partial class NotificationPlaceholders
{
    public const string EmployeeName = "EmployeeName";
    public const string BookTitle = "BookTitle";
    public const string DueDate = "DueDate";
    public const string ReadyUntil = "ReadyUntil";
    public const string Points = "Points";
    public const string TotalPoints = "TotalPoints";
    public const string Reason = "Reason";
    public const string EffectiveUntil = "EffectiveUntil";
    public const string ReturnedDate = "ReturnedDate";

    /// <summary>
    /// The placeholders that have a value for a notification type (NOT-09): what the handlers and the reminder job pass,
    /// plus <see cref="EmployeeName"/> which the creator always adds. A template of the type may use these only.
    /// </summary>
    public static IReadOnlyList<string> AllowedFor(NotificationType type) => type switch
    {
        NotificationType.Welcome or NotificationType.PasswordChanged => [EmployeeName],
        NotificationType.LoanConfirmation or NotificationType.DueSoonReminder or NotificationType.OverdueNotice
            or NotificationType.DueDateCalendarItem => [EmployeeName, BookTitle, DueDate],
        NotificationType.ReservationReady => [EmployeeName, BookTitle, ReadyUntil],
        NotificationType.PenaltyApplied => [EmployeeName, Points, TotalPoints, Reason],
        NotificationType.AccessRestricted => [EmployeeName, Reason],
        NotificationType.AccessSuspended => [EmployeeName, Reason, EffectiveUntil],
        NotificationType.ReturnReceipt => [EmployeeName, BookTitle, ReturnedDate],
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No placeholders are defined for this notification type."),
    };

    /// <summary>Example values for the preview of a template (NOT-09); one for every placeholder of <see cref="AllowedFor"/>.</summary>
    public static IReadOnlyDictionary<string, string> SampleValues { get; } = new Dictionary<string, string>
    {
        [EmployeeName] = "Anna Petrosyan",
        [BookTitle] = "Clean Code",
        [DueDate] = "13.11.2026",
        [ReadyUntil] = "16.11.2026",
        [Points] = "2",
        [TotalPoints] = "5",
        [Reason] = "Returned 2 working days late",
        [EffectiveUntil] = "13.12.2026",
        [ReturnedDate] = "13.11.2026",
    };

    /// <summary>Asia/Yerevan date as shown in emails; the caller converts with IClock.ToLocalDate (NFR-12).</summary>
    public static string FormatDate(DateOnly localDate) => localDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Distinct placeholder names in a template, in order of appearance; also for validating edited templates (NOT-09).</summary>
    public static IReadOnlyList<string> Find(string template) =>
        Pattern().Matches(template).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>False when "{{" or "}}" is left that is not a valid placeholder, such as {{Book.Title}} or an unclosed {{Name.</summary>
    public static bool IsWellFormed(string template)
    {
        var rest = Pattern().Replace(template, string.Empty);
        return !rest.Contains("{{", StringComparison.Ordinal) && !rest.Contains("}}", StringComparison.Ordinal);
    }

    // Spaces inside the braces are allowed: {{ BookTitle }} is the same as {{BookTitle}}.
    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    internal static partial Regex Pattern();
}
