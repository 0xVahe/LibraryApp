namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// Ameriabank email frame around a rendered body (NOT-02). Tables and inline styles because Outlook ignores most CSS.
/// </summary>
internal static class NotificationEmailLayout
{
    private const string Start =
        "<!DOCTYPE html>" +
        "<html lang=\"en\"><head><meta charset=\"utf-8\"></head>" +
        "<body style=\"margin:0;padding:0;background:#f4f5f7;font-family:Arial,Helvetica,sans-serif;color:#1f2933;\">" +
        "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f5f7;padding:24px 0;\">" +
        "<tr><td align=\"center\">" +
        "<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#ffffff;\">" +
        "<tr><td style=\"background:#0a2240;color:#ffffff;padding:20px 32px;font-size:18px;font-weight:bold;\">Ameriabank Corporate Library</td></tr>" +
        "<tr><td style=\"padding:28px 32px;font-size:15px;line-height:1.5;\">";

    private const string End =
        "</td></tr>" +
        "<tr><td style=\"padding:16px 32px;font-size:12px;color:#6b7280;border-top:1px solid #e5e7eb;\">" +
        "This is an automatic message from the Ameriabank corporate library. Please do not reply to this email." +
        "</td></tr>" +
        "</table></td></tr></table></body></html>";

    public static string Wrap(string body) => Start + body + End;
}
