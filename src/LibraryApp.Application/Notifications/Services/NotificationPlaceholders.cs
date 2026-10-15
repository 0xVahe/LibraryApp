using System.Globalization;
using System.Text.RegularExpressions;

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
