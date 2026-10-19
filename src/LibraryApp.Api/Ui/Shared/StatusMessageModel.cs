namespace LibraryApp.Api.Ui.Shared;

/// <summary>Content of the Error, Not found and Forbidden pages (demo <c>not-found.html</c>).</summary>
public sealed record StatusMessageModel(int Code, string Title, string Text, string? TraceId = null)
{
    public static StatusMessageModel NotFound() =>
        new(404, "Page not found", "The page was moved or never existed. Check the address or go back to the start page.");

    public static StatusMessageModel Forbidden() =>
        new(403, "You don’t have access to this page", "Your role does not include this page. Ask the library administrator if you need it.");

    /// <summary>Any other error: the trace id lets the team find the request in the log (NFR-10); no details are shown.</summary>
    public static StatusMessageModel For(int statusCode, string traceId) => statusCode switch
    {
        404 => NotFound(),
        403 => Forbidden(),
        < 500 => new(statusCode, "The request could not be handled", "Go back and try again.", traceId),
        _ => new(statusCode, "Something went wrong", "The error was recorded. Try again in a few minutes.", traceId),
    };
}
