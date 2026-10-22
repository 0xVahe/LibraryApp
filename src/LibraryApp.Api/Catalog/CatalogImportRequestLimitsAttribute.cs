using LibraryApp.Application.Catalog.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Catalog;

/// <summary>
/// Sets the request and form limits of the book CSV import (CAT-09) from <c>Catalog:Import:MaxFileBytes</c> before
/// model binding and the antiforgery check read the form, as the Staff import does.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class CatalogImportRequestLimitsAttribute : TypeFilterAttribute
{
    /// <param name="forPage">True on the Import books page, false on the API endpoint.</param>
    public CatalogImportRequestLimitsAttribute(bool forPage = false) : base(typeof(CatalogImportRequestLimitsFilter))
    {
        Arguments = [forPage];
        Order = 900;
    }
}

/// <summary>
/// The API accepts the file limit plus the multipart envelope. The page accepts five times the file limit, so a file
/// a little over the limit still reaches the import service and gets a row error instead of an error page.
/// </summary>
public sealed class CatalogImportRequestLimitsFilter(IOptions<CatalogImportOptions> options, bool forPage) : IAuthorizationFilter
{
    /// <summary>Multipart envelope allowance of the API, not a business-rule limit; the file itself is checked separately.</summary>
    internal const long MultipartOverheadBytes = 100_000;

    /// <summary>How many times the file limit the Import page accepts.</summary>
    internal const int PageFactor = 5;

    public static long RequestLimit(CatalogImportOptions import, bool forPage) =>
        forPage ? PageFactor * import.MaxFileBytes : import.MaxFileBytes + MultipartOverheadBytes;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method))
            return;

        var limit = RequestLimit(options.Value, forPage);
        var requestLimit = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (requestLimit is { IsReadOnly: false })
            requestLimit.MaxRequestBodySize = limit;

        context.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
        {
            MultipartBodyLengthLimit = limit,
        }));
    }
}
