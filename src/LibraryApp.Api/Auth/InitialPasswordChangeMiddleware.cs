using LibraryApp.Api.Ui;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Auth;

/// <summary>
/// Limits newly enrolled accounts to the initial password change until they choose a personal password.
/// API: only POST /api/auth/change-password; anything else answers 403 <c>staff.password_change_required</c>.
/// Pages (UI guide, Part 0 "First login"): every page redirects to the change-password page, except that page, sign-out
/// and the status pages — so the account can still sign out and see an error page.
/// </summary>
public sealed class InitialPasswordChangeMiddleware(RequestDelegate next)
{
    private static readonly string[] OpenPages = [UiPaths.ChangePassword, UiPaths.Logout, UiPaths.Error, UiPaths.NotFound, UiPaths.Forbidden];

    public async Task InvokeAsync(HttpContext context)
    {
        var requiresChange = context.User.Identity?.IsAuthenticated == true &&
            context.User.HasClaim(LibraryClaimTypes.PasswordChangeRequired, bool.TrueString);
        if (!requiresChange)
        {
            await next(context);
            return;
        }

        if (UiPaths.IsApi(context))
        {
            var isChangeEndpoint = HttpMethods.IsPost(context.Request.Method) &&
                context.Request.Path.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase);
            if (!isChangeEndpoint)
                throw new DomainException(StaffErrors.PasswordChangeRequired);
        }
        else if (context.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>() is not null &&
                 !OpenPages.Any(page => context.Request.Path.StartsWithSegments(page, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect(UiPaths.ChangePassword);
            return;
        }

        await next(context);
    }
}
