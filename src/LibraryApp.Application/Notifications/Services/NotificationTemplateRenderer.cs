using System.Net;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Services;

/// <inheritdoc cref="INotificationTemplateRenderer"/>
public sealed class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    public RenderedNotification Render(string subjectTemplate, string bodyTemplate, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(subjectTemplate);
        ArgumentNullException.ThrowIfNull(bodyTemplate);
        ArgumentNullException.ThrowIfNull(values);

        // Otherwise the raw braces would reach the employee (templates are editable, NOT-09).
        if (!NotificationPlaceholders.IsWellFormed(subjectTemplate) || !NotificationPlaceholders.IsWellFormed(bodyTemplate))
            throw new ArgumentException("The template has a malformed placeholder; use {{Name}} with letters, digits or _ only.");

        // Names only in the message: the values can be personal data (NFR-06).
        var missing = NotificationPlaceholders.Find(subjectTemplate)
            .Concat(NotificationPlaceholders.Find(bodyTemplate))
            .Distinct(StringComparer.Ordinal)
            .Where(name => !values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"No value for placeholder(s): {string.Join(", ", missing)}.", nameof(values));

        var subject = OneLine(Replace(subjectTemplate, values, encode: false));
        if (subject.Length > Notification.SubjectMaxLength)
        {
            subject = subject[..Notification.SubjectMaxLength];
            // Never keep half of an emoji (surrogate pair) at the cut.
            if (char.IsHighSurrogate(subject[^1]))
                subject = subject[..^1];
            subject = subject.TrimEnd();
        }

        var body = Replace(bodyTemplate, values, encode: true);
        return new RenderedNotification(subject, NotificationEmailLayout.Wrap(body));
    }

    // The subject is plain text; the body is HTML, so a title like "C# <in depth>" must not break it.
    private static string Replace(string template, IReadOnlyDictionary<string, string> values, bool encode) =>
        NotificationPlaceholders.Pattern().Replace(template, match =>
        {
            var value = values[match.Groups[1].Value];
            return encode ? WebUtility.HtmlEncode(value) : value;
        });

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
