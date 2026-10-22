using System.Net;
using LibraryApp.Api.Staff;
using LibraryApp.Application.Staff.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class StaffImportRequestLimitsTests
{
    [Fact]
    public async Task A_configured_limit_above_the_old_fixed_ceiling_reaches_model_binding()
    {
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<StaffOptions>(options => options.Import.MaxFileBytes = 2_000_000);
                services.AddControllers().AddApplicationPart(typeof(StaffUploadProbeController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
        using var client = server.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[1_200_000]), "file", "staff.csv");

        using var response = await client.PostAsync("/staff-upload-probe", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1200000", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Configured_request_and_form_limits_are_set_before_the_body_is_read()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.ContentType = "multipart/form-data; boundary=test";
        var feature = new BodyLimitFeature();
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var filter = new StaffImportRequestLimitsFilter(Options.Create(new StaffOptions
        {
            Import = new() { MaxFileBytes = 4096 }
        }));
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

        filter.OnAuthorization(context);

        Assert.Equal(104096, feature.MaxRequestBodySize);
        Assert.IsType<FormFeature>(http.Features.Get<IFormFeature>());
        Assert.Equal(900, new StaffImportRequestLimitsAttribute().Order);
    }

    private sealed class BodyLimitFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
}

// Only added to the isolated TestServer's MVC application parts, never to the real API.
[ApiController]
[Route("staff-upload-probe")]
[StaffImportRequestLimits]
public sealed class StaffUploadProbeController : ControllerBase
{
    [HttpPost]
    public ActionResult<long> Upload(IFormFile file) => Ok(file.Length);
}
