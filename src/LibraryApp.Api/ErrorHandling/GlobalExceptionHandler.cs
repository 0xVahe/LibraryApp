using System.Diagnostics;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.ErrorHandling;

/// <summary>
/// Turns every exception into an RFC 7807 ProblemDetails response with <c>code</c> and <c>traceId</c> (NFR-07, SD-05).
/// A module never mentions HTTP: it throws <see cref="DomainException"/>, and the status comes from <see cref="Error.Kind"/>:
/// Validation 400, Forbidden 403, NotFound 404, Conflict 409, BusinessRule 422. Anything else is 500 with no details
/// (the exception is logged with the trace id, never sent to the client).
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        ProblemDetails body;

        switch (exception)
        {
            case DomainException domain:
                var (status, title) = Describe(domain.Error.Kind);
                body = Problem(status, title, domain.Error.Message, domain.Error.Code);
                logger.LogInformation("Request failed with {Code}: {Message}", domain.Error.Code, domain.Error.Message);
                break;

            case BadHttpRequestException badRequest:
                body = Problem(badRequest.StatusCode, "Bad request", "The request could not be read.", "bad_request");
                logger.LogInformation(badRequest, "Bad request");
                break;

            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                // The client went away; nobody reads the answer.
                return true;

            default:
                body = Problem(StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.", "internal_error");
                logger.LogError(exception, "Unhandled exception");
                break;
        }

        httpContext.Response.StatusCode = body.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = body,
            Exception = exception,
        });
    }

    public static ProblemDetails Problem(int status, string title, string detail, string code) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Type = $"urn:library:error:{code}",
        Extensions = { ["code"] = code },
    };

    /// <summary>Adds <c>traceId</c> to every ProblemDetails the API writes (errors, validation, 404 of routing).</summary>
    public static void AddTraceId(ProblemDetailsContext context) =>
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

    private static (int Status, string Title) Describe(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
        ErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
        ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
        ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
        _ => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
    };
}
