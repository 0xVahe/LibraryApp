namespace LibraryApp.Api.Ui;

/// <summary>
/// One rule splits the host in two: <c>/api/*</c> is the REST API (JWT, ProblemDetails), every other path is the
/// browser UI (cookie, HTML pages). The authentication scheme, the error answers and the status pages all ask here.
/// </summary>
public static class UiPaths
{
    public const string Login = "/login";
    public const string Logout = "/logout";
    public const string Error = "/error";
    public const string NotFound = "/notfound";
    public const string Forbidden = "/forbidden";
    public const string ChangePassword = "/change-password";

    /// <summary>Path prefixes that never belong to a page.</summary>
    public static readonly IReadOnlyList<string> Reserved = ["/api", "/swagger", "/health"];

    public static bool IsApi(PathString path) => path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    public static bool IsApi(HttpContext context) => IsApi(context.Request.Path);
}
