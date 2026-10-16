using LibraryApp.Api.Ui.Auth;
using LibraryApp.Api.Ui.Forms;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Ui;

/// <summary>
/// The browser UI in the API host (Razor Pages, UI guide Part 0): cookie sign-in next to the JWT, chosen by path; page
/// policies; validation and domain errors for forms; HTML error pages; static files. Integration Owner (shared shell).
/// Groups add pages in <c>Areas/&lt;Module&gt;</c> and never change this file.
/// </summary>
public static class UiServiceCollectionExtensions
{
    public static IServiceCollection AddUi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<UiOptions>()
            .Bind(configuration.GetSection(UiOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<UiOptions>, UiOptionsValidator>();

        UiAuthentication.Add(services, environment);
        services.AddAuthorization(UiPolicies.Add);
        services.AddMemoryCache();
        services.AddScoped<IUiSignIn, UiSignIn>();

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "LibraryApp.Antiforgery";
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });

        services.AddRazorPages().AddMvcOptions(options =>
        {
            options.Filters.Add<UiValidationFilter>();
            options.Filters.Add<UiDomainExceptionFilter>();
        });
        return services;
    }

    /// <summary>
    /// Error answers by path: <c>/api</c> keeps ProblemDetails (NFR-07); pages get the Error page for an exception and
    /// the Not found / Forbidden / Error page for an empty 4xx/5xx answer. Replaces UseExceptionHandler and UseStatusCodePages.
    /// </summary>
    public static WebApplication UseErrorAnswers(this WebApplication app)
    {
        // The global handler answers /api itself; for pages it declines, and the request is re-run as the Error page.
        app.UseExceptionHandler(UiPaths.Error);
        app.UseWhen(UiPaths.IsApi, api => api.UseStatusCodePages());   // empty 401/403/404 answers get a ProblemDetails body
        app.UseWhen(context => !UiPaths.IsApi(context), ui => ui.UseStatusCodePagesWithReExecute(UiPaths.Error, "?statusCode={0}"));
        return app;
    }

    /// <summary>
    /// Static files (<c>wwwroot</c>: site.css, fonts), the security headers of the pages, then routing. Routing is
    /// placed here on purpose, after the error answers: a request re-run as the Error page must be routed again.
    /// </summary>
    public static WebApplication UseUi(this WebApplication app)
    {
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            if (!UiPaths.IsApi(context) && !context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
            {
                var headers = context.Response.Headers;
                // Pages use no scripts and no inline styles (design.md); nothing loads from another site.
                headers.ContentSecurityPolicy =
                    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
                    "form-action 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }

            await next(context);
        });
        app.UseRouting();
        return app;
    }

    /// <summary>Every page requires a signed-in user unless its PageModel has <c>[AllowAnonymous]</c> (NFR-02 for the UI).</summary>
    public static WebApplication MapUi(this WebApplication app)
    {
        app.MapRazorPages().RequireAuthorization(UiPolicies.SignedIn);
        return app;
    }
}
