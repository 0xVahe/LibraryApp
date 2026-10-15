using Microsoft.AspNetCore.WebUtilities;

namespace LibraryApp.Api.ErrorHandling;

/// <summary>An error answer of the HTTP pipeline itself: its status, stable code, title and default detail.</summary>
public sealed record HttpError(int Status, string Code, string Title, string Detail);

/// <summary>
/// Errors that only the HTTP pipeline produces: no token, wrong method, unreadable body, unknown route, crash (NFR-07, SD-05).
/// Codes have no module prefix, like <c>CommonErrors</c>. Modules never use these: they throw <c>DomainException</c> with
/// an error of their own <c>&lt;Module&gt;Errors</c> class (or of <c>CommonErrors</c>), and the status comes from its
/// <c>ErrorKind</c>. A new entry here is a platform change (Integration Owner).
/// </summary>
public static class HttpErrors
{
    public static readonly HttpError BadRequest = new(StatusCodes.Status400BadRequest, "bad_request", "Bad request",
        "The request could not be read.");

    public static readonly HttpError Unauthorized = new(StatusCodes.Status401Unauthorized, "unauthorized", "Unauthorized",
        "Sign in to use this endpoint.");

    public static readonly HttpError Forbidden = new(StatusCodes.Status403Forbidden, "forbidden", "Forbidden",
        "Your role does not allow this operation.");

    public static readonly HttpError NotFound = new(StatusCodes.Status404NotFound, "not_found", "Not found",
        "The resource does not exist.");

    public static readonly HttpError MethodNotAllowed = new(StatusCodes.Status405MethodNotAllowed, "method_not_allowed",
        "Method not allowed", "The endpoint does not support this HTTP method.");

    public static readonly HttpError PayloadTooLarge = new(StatusCodes.Status413PayloadTooLarge, "payload_too_large",
        "Payload too large", "The request body is larger than the endpoint accepts.");

    public static readonly HttpError UnsupportedMediaType = new(StatusCodes.Status415UnsupportedMediaType,
        "unsupported_media_type", "Unsupported media type", "The endpoint does not accept this content type.");

    public static readonly HttpError TooManyRequests = new(StatusCodes.Status429TooManyRequests, "too_many_requests",
        "Too many requests", "Too many requests. Try again later.");

    public static readonly HttpError InternalError = new(StatusCodes.Status500InternalServerError, "internal_error",
        "Unexpected error", "An unexpected error occurred.");

    private static readonly Dictionary<int, HttpError> ByStatus = new[]
    {
        BadRequest, Unauthorized, Forbidden, NotFound, MethodNotAllowed, PayloadTooLarge, UnsupportedMediaType,
        TooManyRequests, InternalError,
    }.ToDictionary(error => error.Status);

    /// <summary>Every known error of the pipeline, for tests and documentation.</summary>
    public static IReadOnlyCollection<HttpError> All => ByStatus.Values;

    /// <summary>
    /// The error for a status. A status without its own entry still gets a stable code (<c>http_&lt;status&gt;</c>) and
    /// its reason phrase, so no error answer ever goes out without <c>code</c>.
    /// </summary>
    public static HttpError For(int status) =>
        ByStatus.TryGetValue(status, out var error)
            ? error
            : new HttpError(status, $"http_{status}", ReasonPhrases.GetReasonPhrase(status), ReasonPhrases.GetReasonPhrase(status));

    /// <summary>The ProblemDetails <c>type</c> of a code: one format for module, platform and pipeline errors.</summary>
    public static string TypeFor(string code) => $"urn:library:error:{code}";
}
