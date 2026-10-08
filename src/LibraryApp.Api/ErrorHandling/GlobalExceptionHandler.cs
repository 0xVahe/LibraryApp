using System.Diagnostics;
using System.Text.Json;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Api.ErrorHandling;

/// <summary>
/// Turns every exception into an RFC 7807 ProblemDetails response with <c>code</c> and <c>traceId</c> (NFR-07, SD-05).
/// A module never mentions HTTP: it throws <see cref="DomainException"/>, and the status comes from <see cref="Error.Kind"/>:
/// Validation 400, Forbidden 403, NotFound 404, Conflict 409, BusinessRule 422. Anything else is 500 with no details
/// (the exception is logged with the trace id, never sent to the client).
/// <para>
/// Safety net for database races that a module did not translate itself: a concurrency conflict (row version) and a
/// unique index violation become 409. A module that knows the meaning throws its own error instead
/// (for example <c>catalog.isbn_duplicate</c>).
/// </para>
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
                if (Describe(domain.Error.Kind) is not { } mapped)
                {
                    // A new ErrorKind without an HTTP status: fail loudly instead of guessing one.
                    body = Problem(StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.", "internal_error");
                    logger.LogError(exception, "ErrorKind {Kind} has no HTTP status in GlobalExceptionHandler", domain.Error.Kind);
                    break;
                }

                body = Problem(mapped.Status, mapped.Title, domain.Error.Message, domain.Error.Code);
                logger.LogInformation("Request failed with {Code}: {Message}", domain.Error.Code, domain.Error.Message);
                break;

            case DbUpdateConcurrencyException concurrency:
                body = Problem(StatusCodes.Status409Conflict, "Conflict",
                    "The data was changed by someone else. Reload it and try again.", "concurrency_conflict");
                logger.LogWarning("Concurrency conflict on {Entities}", EntityNames(concurrency));
                break;

            case DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } sql } duplicate:
                // The SQL message contains the duplicate value (may be personal data, NFR-06): log the number and entities only.
                body = Problem(StatusCodes.Status409Conflict, "Conflict",
                    "An item with the same unique value already exists.", "duplicate_value");
                logger.LogWarning("Unique index violation (SQL {Number}) on {Entities}", sql.Number, EntityNames(duplicate));
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
        var context = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = body,
            Exception = exception,
        };
        if (await problemDetails.TryWriteAsync(context))
            return true;

        // No writer took it (the client accepts neither JSON nor */*, e.g. Swagger UI sends Accept: text/plain).
        // Returning false would let the middleware write its own "status: 500" body under our status: write ours.
        AddTraceId(context);
        await httpContext.Response.WriteAsJsonAsync(body, (JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
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

    /// <summary>HTTP status of every <see cref="ErrorKind"/>; a new kind is added here in the same pull request.</summary>
    private static (int Status, string Title)? Describe(ErrorKind kind) => kind switch
    {
        ErrorKind.BusinessRule => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
        ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
        ErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
        ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
        ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
        _ => null,
    };

    private static string EntityNames(DbUpdateException exception) =>
        string.Join(", ", exception.Entries.Select(e => e.Metadata.ClrType.Name).Distinct());
}
