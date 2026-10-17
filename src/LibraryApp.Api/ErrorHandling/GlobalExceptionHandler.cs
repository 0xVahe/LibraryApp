using System.Diagnostics;
using LibraryApp.Api.Ui;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Api.ErrorHandling;

/// <summary>
/// Turns every exception into an RFC 7807 ProblemDetails response with <c>code</c> and <c>traceId</c> (NFR-07, SD-05).
/// A module never mentions HTTP: it throws <see cref="DomainException"/>, and the status comes from <see cref="Error.Kind"/>:
/// Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, BusinessRule 422. Anything else is 500 with no details
/// (the exception is logged with the trace id, never sent to the client).
/// <para>
/// Safety net for database races that a module did not translate itself: a concurrency conflict (row version) and a
/// unique index violation become the same 409 errors the unit of work gives (<see cref="CommonErrors"/>). A module that
/// knows the meaning throws its own error instead (for example <c>catalog.book_isbn_duplicate</c>).
/// </para>
/// <para>
/// <b>Modules never change this class.</b> A new error is a field in the module's <c>&lt;Module&gt;Errors</c> class
/// (see <see cref="Error"/>); extra data for the client goes into <see cref="DomainException.Extensions"/>. Only a new
/// <see cref="ErrorKind"/> or a new pipeline error (<see cref="HttpErrors"/>) changes this folder — a platform change.
/// </para>
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        // Pages are answered by the Error page (UseErrorAnswers re-runs the request there); the API gets ProblemDetails.
        // The original path: the middleware has already set Request.Path to the Error page when handlers run.
        var path = httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? httpContext.Request.Path;
        if (!UiPaths.IsApi(path))
            return false;

        ProblemDetails body;

        switch (exception)
        {
            case DomainException domain:
                if (Describe(domain.Error.Kind) is not { } mapped)
                {
                    // A new ErrorKind without an HTTP status: fail loudly instead of guessing one.
                    body = Problem(HttpErrors.InternalError);
                    logger.LogError(exception, "ErrorKind {Kind} has no HTTP status in GlobalExceptionHandler", domain.Error.Kind);
                    break;
                }

                body = Problem(mapped.Status, mapped.Title, domain.Error.Message, domain.Error.Code);
                foreach (var extension in domain.Extensions)
                    body.Extensions.TryAdd(extension.Key, extension.Value);
                logger.LogInformation("Request failed with {Code}: {Message}", domain.Error.Code, domain.Error.Message);
                break;

            case DbUpdateConcurrencyException concurrency:
                body = Problem(CommonErrors.ConcurrencyConflict);
                logger.LogWarning("Concurrency conflict on {Entities}", EntityNames(concurrency));
                break;

            case DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } sql } duplicate:
                // The SQL message contains the duplicate value (may be personal data, NFR-06): log the number and entities only.
                body = Problem(CommonErrors.UniqueViolation);
                logger.LogWarning("Unique index violation (SQL {Number}) on {Entities}", sql.Number, EntityNames(duplicate));
                break;

            case BadHttpRequestException badRequest:
                // Unreadable body (400) or a body over the size limit (413): the status of the exception decides.
                body = Problem(HttpErrors.For(badRequest.StatusCode));
                logger.LogInformation(badRequest, "Bad request");
                break;

            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                // The client went away; nobody reads the answer.
                return true;

            default:
                body = Problem(HttpErrors.InternalError);
                logger.LogError(exception, "Unhandled exception");
                break;
        }

        httpContext.Response.StatusCode = body.Status!.Value;

        // Always written: AnyClientProblemDetailsWriter takes what the built-in writers refuse (Accept: text/plain).
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = body,
            Exception = exception,
        });
    }

    /// <summary>The ProblemDetails every error answer of the API uses: status, title, detail, <c>type</c> and <c>code</c>.</summary>
    public static ProblemDetails Problem(int status, string title, string detail, string code) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Type = HttpErrors.TypeFor(code),
        Extensions = { ["code"] = code },
    };

    /// <summary>A pipeline error (<see cref="HttpErrors"/>) with its default detail, or a more precise one.</summary>
    public static ProblemDetails Problem(HttpError error, string? detail = null) =>
        Problem(error.Status, error.Title, detail ?? error.Detail, error.Code);

    /// <summary>A platform or module error with the status and title of its kind.</summary>
    public static ProblemDetails Problem(Error error)
    {
        var (status, title) = Describe(error.Kind) ?? (HttpErrors.InternalError.Status, HttpErrors.InternalError.Title);
        return Problem(status, title, error.Message, error.Code);
    }

    /// <summary>
    /// Runs for every ProblemDetails the API writes. An answer without a body (no token, wrong role, unknown route,
    /// wrong method or content type ...) gets the code, type and title of <see cref="HttpErrors"/>, so every error has
    /// <c>code</c>; a code that is already set is never replaced. Every answer gets <c>traceId</c>.
    /// </summary>
    public static void CustomizeProblemDetails(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        if (!problem.Extensions.ContainsKey("code") && problem.Status is int status && status >= 400)
        {
            var error = HttpErrors.For(status);
            problem.Extensions["code"] = error.Code;
            problem.Type = HttpErrors.TypeFor(error.Code);
            problem.Title = error.Title;
        }

        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    }

    /// <summary>HTTP status and title of every <see cref="ErrorKind"/>; a new kind is added here in the same pull request.</summary>
    private static (int Status, string Title)? Describe(ErrorKind kind) => kind switch
    {
        ErrorKind.BusinessRule => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
        ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
        ErrorKind.Unauthorized => (HttpErrors.Unauthorized.Status, HttpErrors.Unauthorized.Title),
        ErrorKind.Forbidden => (HttpErrors.Forbidden.Status, HttpErrors.Forbidden.Title),
        ErrorKind.NotFound => (HttpErrors.NotFound.Status, HttpErrors.NotFound.Title),
        ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
        _ => null,
    };

    private static string EntityNames(DbUpdateException exception) =>
        string.Join(", ", exception.Entries.Select(e => e.Metadata.ClrType.Name).Distinct());
}
