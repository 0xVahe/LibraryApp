namespace LibraryApp.Application.Abstractions.Notifications;

/// <summary>
/// One email to a staff member (INT-08). Personal data (NFR-06): never log To, Cc, the body or an attachment.
/// <paramref name="Attachments"/> is optional (NOT-07); null or empty means no attachment.
/// </summary>
public sealed record OutlookMessage(
    string To,
    string? Cc,
    string Subject,
    string HtmlBody,
    IReadOnlyList<OutlookAttachment>? Attachments = null);

/// <summary>A text file attached to an email (NOT-07), for example the calendar item <c>due-date.ics</c>.</summary>
/// <param name="FileName">The name the recipient sees.</param>
/// <param name="MediaType">For example <c>text/calendar; method=PUBLISH</c>.</param>
/// <param name="Text">The content; sent as UTF-8.</param>
public sealed record OutlookAttachment(string FileName, string MediaType, string Text);
