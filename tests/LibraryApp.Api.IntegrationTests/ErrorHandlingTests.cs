using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using LibraryApp.Api.ErrorHandling;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>The real API with a test controller: every error becomes ProblemDetails with code and traceId (NFR-07, SD-05, SD-15).</summary>
public sealed class ErrorHandlingFactory : ApiHostFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddControllers().AddApplicationPart(typeof(TestErrorsController).Assembly);
        services.AddScoped<IValidator<TestRequest>, TestRequestValidator>();
    }
}

public sealed record TestRequest(string? Name, int Quantity);

public sealed class TestRequestValidator : AbstractValidator<TestRequest>
{
    public TestRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 10);
    }
}

[ApiController]
[AllowAnonymous]
[Route("test-errors")]
public sealed class TestErrorsController : ControllerBase
{
    [HttpGet("{kind}")]
    public IActionResult Throw(string kind) => kind switch
    {
        "business" => throw new DomainException(Error.BusinessRule("test.rule", "Maximum renewals reached")),
        "validation" => throw new DomainException(Error.Validation("test.invalid", "Invalid")),
        "unauthorized" => throw new DomainException(Error.Unauthorized("test.unauthorized", "Login required")),
        "forbidden" => throw new DomainException(Error.Forbidden("test.forbidden", "Not yours")),
        "notfound" => throw new DomainException(Error.NotFound("test.not_found", "No such loan")),
        "conflict" => throw new DomainException(Error.Conflict("test.conflict", "Already returned")),
        "extensions" => throw new DomainException(Error.Conflict("test.conflict", "Already returned"),
            extensions: new Dictionary<string, object?>
            {
                ["outstandingBooks"] = new[] { new { title = "Clean Code", barcode = "BK-001" } },
                ["code"] = "replacement_code",
                ["traceId"] = "replacement_trace",
            }),
        "concurrency" => throw new DbUpdateConcurrencyException("secret internal detail"),
        _ => throw new InvalidOperationException("secret internal detail"),
    };

    [HttpGet("kind/{kind:int}")]
    public IActionResult ThrowKind(int kind) => throw new DomainException(new Error("test.kind", "Kind", (ErrorKind)kind));

    /// <summary>Two active templates of one type in one save: SQL Server rejects the second (filtered unique index).</summary>
    [HttpPost("duplicate")]
    public async Task<IActionResult> Duplicate([FromServices] LibraryDbContext db)
    {
        db.NotificationTemplates.AddRange(
            new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "secret-subject", "Body"),
            new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "secret-subject", "Body"));
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost]
    public IActionResult Post(TestRequest request) => Ok(request);
}

/// <summary>Needs a token like every real endpoint: without one the answer is an empty 401 from authentication.</summary>
[ApiController]
[Route("test-secured")]
public sealed class TestSecuredController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

public class ErrorHandlingTests(ErrorHandlingFactory factory) : IClassFixture<ErrorHandlingFactory>
{
    [Theory]
    [InlineData("business", HttpStatusCode.UnprocessableEntity, "test.rule")]
    [InlineData("validation", HttpStatusCode.BadRequest, "test.invalid")]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized, "test.unauthorized")]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "test.forbidden")]
    [InlineData("notfound", HttpStatusCode.NotFound, "test.not_found")]
    [InlineData("conflict", HttpStatusCode.Conflict, "test.conflict")]
    public async Task A_domain_error_becomes_ProblemDetails_with_the_status_of_its_kind(string kind, HttpStatusCode status, string code)
    {
        var response = await factory.CreateClient().GetAsync($"/test-errors/{kind}");
        var body = await ReadAsync(response);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal($"urn:library:error:{code}", body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Domain_error_details_are_forwarded_without_replacing_code_or_trace_id()
    {
        var response = await factory.CreateClient().GetAsync("/test-errors/extensions");
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("test.conflict", body.GetProperty("code").GetString());
        var traceId = body.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrEmpty(traceId));
        Assert.NotEqual("replacement_trace", traceId);
        var book = Assert.Single(body.GetProperty("outstandingBooks").EnumerateArray());
        Assert.Equal("Clean Code", book.GetProperty("title").GetString());
        Assert.Equal("BK-001", book.GetProperty("barcode").GetString());
    }

    /// <summary>
    /// Swagger UI sends Accept: text/plain. No ProblemDetails writer accepts it; the answer must still be our body,
    /// not the default "status: 500" body of the exception handler middleware under a 409 status.
    /// </summary>
    [Theory]
    [InlineData("conflict", HttpStatusCode.Conflict, "test.conflict")]
    [InlineData("boom", HttpStatusCode.InternalServerError, "internal_error")]
    public async Task An_error_is_ProblemDetails_whatever_the_client_accepts(string kind, HttpStatusCode status, string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/test-errors/{kind}");
        request.Headers.Accept.ParseAdd("text/plain");

        var response = await factory.CreateClient().SendAsync(request);
        var body = await ReadAsync(response);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task A_domain_error_is_logged_with_its_final_status_not_as_500()
    {
        var before = ApiHostFactory.Logs.Events.Count;
        await factory.CreateClient().GetAsync("/test-errors/conflict");

        var completed = await RequestLogAsync("/test-errors/conflict", before);
        Assert.Equal("409", completed.Properties["StatusCode"].ToString());
        Assert.Equal(LogEventLevel.Information, completed.Level);
        Assert.Null(completed.Exception);
    }

    /// <summary>
    /// Test classes run in parallel, each with its own host. A host that stops calls Log.CloseAndFlush, which makes
    /// the static Log.Logger silent; the request log of the hosts still running must not depend on it.
    /// </summary>
    [Fact]
    public async Task The_request_log_survives_another_host_stopping()
    {
        var other = new ApiHostFactory();
        await other.InitializeAsync();
        await other.DisposeAsync();

        var before = ApiHostFactory.Logs.Events.Count;
        await factory.CreateClient().GetAsync("/test-errors/notfound");

        var completed = await RequestLogAsync("/test-errors/notfound", before);
        Assert.Equal("404", completed.Properties["StatusCode"].ToString());
    }

    [Fact]
    public async Task Every_error_kind_has_an_http_status()
    {
        var client = factory.CreateClient();

        foreach (var kind in Enum.GetValues<ErrorKind>())
        {
            var response = await client.GetAsync($"/test-errors/kind/{(int)kind}");

            Assert.True(response.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError,
                $"ErrorKind.{kind} answers {(int)response.StatusCode}: add its status to GlobalExceptionHandler.Describe");
        }
    }

    [Fact]
    public async Task An_unmapped_error_kind_is_500_not_a_guessed_status()
    {
        var response = await factory.CreateClient().GetAsync("/test-errors/kind/999");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task A_concurrency_conflict_is_409_without_internal_details()
    {
        var response = await factory.CreateClient().GetAsync("/test-errors/concurrency");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("concurrency_conflict", JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret internal detail", text);
    }

    /// <summary>Pipeline codes are platform codes: no module prefix, one per status, never the same as a CommonErrors code.</summary>
    [Fact]
    public void Pipeline_errors_have_unique_platform_codes()
    {
        var codes = HttpErrors.All.Select(error => error.Code).ToList();
        var platformCodes = typeof(CommonErrors).GetFields().Select(field => ((Error)field.GetValue(null)!).Code);

        Assert.All(codes, code => Assert.Matches("^[a-z][a-z0-9_]*$", code));
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Empty(codes.Intersect(platformCodes));
    }

    [Theory]
    [InlineData(413, "payload_too_large")]
    [InlineData(418, "http_418")]
    [InlineData(503, "http_503")]
    public void Every_error_status_gets_a_code(int status, string code)
    {
        var error = HttpErrors.For(status);

        Assert.Equal(code, error.Code);
        Assert.False(string.IsNullOrEmpty(error.Title));
    }

    [Fact]
    public async Task A_unique_index_violation_is_409_without_the_duplicate_value()
    {
        var response = await factory.CreateClient().PostAsync("/test-errors/duplicate", null);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("unique_violation", JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("UX_NotificationTemplates", text);
    }

    [Fact]
    public async Task An_acceptance_criteria_message_reaches_the_client_unchanged()
    {
        var body = await ReadAsync(await factory.CreateClient().GetAsync("/test-errors/business"));

        Assert.Equal("Maximum renewals reached", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_unexpected_exception_is_500_without_internal_details()
    {
        var response = await factory.CreateClient().GetAsync("/test-errors/boom");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("internal_error", text);
        Assert.DoesNotContain("secret internal detail", text);
        Assert.DoesNotContain("   at ", text);   // no stack trace
    }

    [Fact]
    public async Task Invalid_input_is_400_with_errors_per_field()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/test-errors", new { name = "", quantity = 99 });
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("Name", out _));
        Assert.True(body.GetProperty("errors").TryGetProperty("Quantity", out _));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Valid_input_reaches_the_action()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/test-errors", new { name = "ok", quantity = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unreadable_input_has_the_same_shape_as_validation_errors()
    {
        var response = await factory.CreateClient().PostAsync("/test-errors",
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unknown_route_is_a_ProblemDetails_404()
    {
        var response = await factory.CreateClient().GetAsync("/no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadAsync(response);
        Assert.Equal("not_found", body.GetProperty("code").GetString());
        Assert.Equal("urn:library:error:not_found", body.GetProperty("type").GetString());
    }

    /// <summary>
    /// Empty answers (no route, no token, wrong method, wrong content type) get their body from the status code pages.
    /// With Accept: text/plain no ProblemDetails writer accepted them and the client got the text "Status Code: 404; Not Found".
    /// </summary>
    [Theory]
    [InlineData("GET", "/no-such-endpoint", null, HttpStatusCode.NotFound, "not_found")]
    [InlineData("GET", "/test-secured", null, HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData("DELETE", "/test-errors", null, HttpStatusCode.MethodNotAllowed, "method_not_allowed")]
    [InlineData("POST", "/test-errors", "text/plain", HttpStatusCode.UnsupportedMediaType, "unsupported_media_type")]
    public async Task An_empty_error_answer_is_ProblemDetails_whatever_the_client_accepts(
        string method, string path, string? contentType, HttpStatusCode status, string code)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (contentType is not null)
            request.Content = new StringContent("name=x", System.Text.Encoding.UTF8, contentType);
        request.Headers.Accept.ParseAdd("text/plain");

        var response = await factory.CreateClient().SendAsync(request);
        var body = await ReadAsync(response);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal(HttpErrors.For((int)status).Title, body.GetProperty("title").GetString());   // one title per status
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal($"urn:library:error:{code}", body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    /// <summary>
    /// The "HTTP GET ... responded ..." event of the request logging; written right after the response. Only events
    /// after <paramref name="after"/> count: the shared sink still holds the events of earlier tests on the same path.
    /// </summary>
    private static async Task<LogEvent> RequestLogAsync(string path, int after)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var completed = ApiHostFactory.Logs.Events.Skip(after).LastOrDefault(e =>
                e.Properties.TryGetValue("RequestPath", out var value) && value.ToString() == $"\"{path}\"" &&
                e.Properties.ContainsKey("StatusCode"));
            if (completed is not null)
                return completed;

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException($"No request log event for {path}");
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
