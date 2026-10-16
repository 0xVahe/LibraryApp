using System.Runtime.ExceptionServices;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Ui.Forms;

/// <summary>
/// How a page reacts to a <see cref="DomainException"/> — the same everywhere, like the global handler does for the API.
/// In a POST handler:
/// <code>
/// try { await service.DoAsync(Input, ct); }
/// catch (DomainException ex) { await LoadListsAsync(); return this.DomainError(ex); }
/// </code>
/// </summary>
public static class DomainErrors
{
    /// <summary>
    /// Validation, Conflict, BusinessRule → the message in <c>ModelState</c> and the same page again, with the form filled.
    /// NotFound → the Not found page; Forbidden → the Forbidden page; Unauthorized → sign-in. Any other kind is rethrown
    /// and ends on the Error page.
    /// </summary>
    public static IActionResult DomainError(this PageModel page, DomainException exception)
    {
        switch (exception.Error.Kind)
        {
            case ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule:
                page.ModelState.AddModelError(string.Empty, exception.Error.Message);
                return new PageResult();
            default:
                return ResultFor(exception) ?? Rethrow(exception);
        }
    }

    /// <summary>The result for the kinds every page shows the same way, or null for form errors and unknown kinds.</summary>
    internal static IActionResult? ResultFor(DomainException exception) => exception.Error.Kind switch
    {
        ErrorKind.NotFound => new NotFoundResult(),
        ErrorKind.Forbidden => new ForbidResult(),
        ErrorKind.Unauthorized => new ChallengeResult(),
        _ => null,
    };

    private static IActionResult Rethrow(DomainException exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();
        return null!;
    }
}

/// <summary>
/// A <see cref="DomainException"/> that a page did not catch: NotFound, Forbidden and Unauthorized get their page
/// (a GET of an unknown id needs no try/catch); form errors and anything else go on to the Error page.
/// </summary>
public sealed class UiDomainExceptionFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var executed = await next();
        if (executed is { Exception: DomainException exception, ExceptionHandled: false } && DomainErrors.ResultFor(exception) is { } result)
        {
            executed.Result = result;
            executed.ExceptionHandled = true;
        }
    }
}
