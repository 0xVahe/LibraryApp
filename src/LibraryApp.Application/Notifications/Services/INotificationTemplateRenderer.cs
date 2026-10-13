namespace LibraryApp.Application.Notifications.Services;

/// <summary>Turns a template and its values into a ready email (NOT-02). No database, no clock: the caller passes everything.</summary>
public interface INotificationTemplateRenderer
{
    /// <summary>
    /// Replaces every {{Name}} in the subject and body. Values are HTML-encoded in the body; the subject is one line of at most
    /// 300 characters (G4-07). The body is wrapped in the Ameriabank email layout.
    /// </summary>
    /// <exception cref="ArgumentException">A placeholder in the template has no value; the message names the placeholders only.</exception>
    RenderedNotification Render(string subjectTemplate, string bodyTemplate, IReadOnlyDictionary<string, string> values);
}
