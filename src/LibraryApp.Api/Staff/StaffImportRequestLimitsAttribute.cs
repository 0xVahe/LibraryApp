using LibraryApp.Application.Staff.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Staff;

/// <summary>Sets configured upload limits before model binding and Razor antiforgery read the form.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class StaffImportRequestLimitsAttribute : TypeFilterAttribute
{
    public StaffImportRequestLimitsAttribute() : base(typeof(StaffImportRequestLimitsFilter)) => Order = 900;
}

public sealed class StaffImportRequestLimitsFilter(IOptions<StaffOptions> options) : IAuthorizationFilter
{
    // Multipart envelope allowance, not a business-rule limit. The file itself is checked separately.
    internal const long MultipartOverheadBytes = 100_000;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method))
            return;

        var limit = options.Value.Import.MaxFileBytes + MultipartOverheadBytes;
        var requestLimit = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (requestLimit is { IsReadOnly: false })
            requestLimit.MaxRequestBodySize = limit;

        // The same slack lets normal-sized over-limit files receive the existing field error.
        context.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
        {
            MultipartBodyLengthLimit = limit
        }));
    }
}
