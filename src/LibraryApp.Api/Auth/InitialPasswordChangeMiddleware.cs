using LibraryApp.Infrastructure.Identity;

namespace LibraryApp.Api.Auth;

/// <summary>Limits imported accounts to the initial password-change endpoint until they choose a personal password.</summary>
public sealed class InitialPasswordChangeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requiresChange = context.User.Identity?.IsAuthenticated == true &&
            context.User.HasClaim(LibraryClaimTypes.PasswordChangeRequired, bool.TrueString);
        var isChangeEndpoint = HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase);

        if (requiresChange && !isChangeEndpoint)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
