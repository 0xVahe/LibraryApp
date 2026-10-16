using System.Diagnostics;
using LibraryApp.Api.Ui.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Pages;

/// <summary>
/// Error page of the UI. Reached by re-running the request: after an unhandled exception (500) and for an empty 4xx/5xx
/// answer (<c>?statusCode=</c>). Any method, no antiforgery check: a failed POST is re-run here as a POST.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    public StatusMessageModel Message { get; private set; } = StatusMessageModel.For(500, "");

    public void OnGet(int? statusCode) => Describe(statusCode);

    public void OnPost(int? statusCode) => Describe(statusCode);

    private void Describe(int? statusCode)
    {
        var code = statusCode ?? (Response.StatusCode >= 400 ? Response.StatusCode : 500);
        Message = StatusMessageModel.For(code, Activity.Current?.Id ?? HttpContext.TraceIdentifier);
    }
}
