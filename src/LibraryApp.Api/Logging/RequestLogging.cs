using Microsoft.IdentityModel.JsonWebTokens;
using Serilog.AspNetCore;

namespace LibraryApp.Api.Logging;

/// <summary>
/// NFR-10: every request is logged once with user, route, duration and status. The user is the "sub" claim of the
/// access token (the AppUser id, never the email: no personal data in logs, NFR-06); anonymous requests (login, health,
/// Swagger) are logged as "anonymous".
/// </summary>
public static class RequestLogging
{
    public const string Anonymous = "anonymous";

    public const string MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms for {UserId}";

    public static void Configure(RequestLoggingOptions options, Serilog.ILogger logger)
    {
        options.Logger = logger;
        options.MessageTemplate = MessageTemplate;
        options.EnrichDiagnosticContext = (diagnostics, http) =>
            diagnostics.Set("UserId", http.User.Identity?.IsAuthenticated == true
                ? http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? Anonymous
                : Anonymous);
    }
}
