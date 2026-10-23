using LibraryApp.Api.Catalog;
using LibraryApp.Application.Catalog.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-09: the request and form limits of the book import follow <c>Catalog:Import:MaxFileBytes</c>. No database needed.</summary>
public sealed class CatalogImportRequestLimitsTests
{
    [Theory]
    [InlineData(false, 4096 + 100_000)]   // API: the file limit plus the multipart envelope
    [InlineData(true, 5 * 4096)]          // Import books page: five times the file limit, so the row error is shown
    public void Configured_request_and_form_limits_are_set_before_the_body_is_read(bool forPage, long expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.ContentType = "multipart/form-data; boundary=test";
        var feature = new BodyLimitFeature();
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var filter = new CatalogImportRequestLimitsFilter(
            Options.Create(new CatalogImportOptions { MaxRows = 500, MaxFileBytes = 4096 }), forPage);
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

        filter.OnAuthorization(context);

        Assert.Equal(expected, feature.MaxRequestBodySize);
        Assert.IsType<FormFeature>(http.Features.Get<IFormFeature>());
    }

    [Fact]
    public void A_get_request_keeps_the_host_limits()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        var feature = new BodyLimitFeature { MaxRequestBodySize = 123 };
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var filter = new CatalogImportRequestLimitsFilter(
            Options.Create(new CatalogImportOptions { MaxRows = 500, MaxFileBytes = 4096 }), forPage: true);

        filter.OnAuthorization(new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>()));

        Assert.Equal(123, feature.MaxRequestBodySize);
    }

    [Fact]
    public void The_limits_are_set_before_the_antiforgery_check()
    {
        // The antiforgery filter of Razor Pages runs at order 1000 and reads the form.
        Assert.Equal(900, new CatalogImportRequestLimitsAttribute().Order);
        Assert.Equal(900, new CatalogImportRequestLimitsAttribute(forPage: true).Order);
    }

    private sealed class BodyLimitFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
}
