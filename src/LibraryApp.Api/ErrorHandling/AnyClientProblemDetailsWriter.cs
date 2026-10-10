using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.ErrorHandling;

/// <summary>
/// The last ProblemDetails writer: writes the body when no other writer accepts the request. The built-in writers
/// write JSON only when the client accepts it (Swagger UI sends <c>Accept: text/plain</c>), and the MVC writer only
/// for a controller endpoint. Without this one the exception handler fell back to its own "status: 500" body and the
/// status code pages to the text "Status Code: 404; Not Found". Registered after <c>AddProblemDetails</c>, so it runs last.
/// </summary>
public sealed class AnyClientProblemDetailsWriter(IOptions<ProblemDetailsOptions> options) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Status ??= context.HttpContext.Response.StatusCode;

        // The same type and title the built-in writer gives a status (RFC 9110 link, reason phrase).
        var defaults = TypedResults.Problem(statusCode: problem.Status).ProblemDetails;
        problem.Type ??= defaults.Type;
        problem.Title ??= defaults.Title;

        options.Value.CustomizeProblemDetails?.Invoke(context);

        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            problem, (JsonSerializerOptions?)null, "application/problem+json", context.HttpContext.RequestAborted));
    }
}
