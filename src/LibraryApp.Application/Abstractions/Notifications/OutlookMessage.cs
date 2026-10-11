namespace LibraryApp.Application.Abstractions.Notifications;

/// <summary>One email to a staff member (INT-08). Personal data (NFR-06): never log To, Cc or the body.</summary>
public sealed record OutlookMessage(string To, string? Cc, string Subject, string HtmlBody);
