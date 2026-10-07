using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
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
        "forbidden" => throw new DomainException(Error.Forbidden("test.forbidden", "Not yours")),
        "notfound" => throw new DomainException(Error.NotFound("test.not_found", "No such loan")),
        "conflict" => throw new DomainException(Error.Conflict("test.conflict", "Already returned")),
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

public class ErrorHandlingTests(ErrorHandlingFactory factory) : IClassFixture<ErrorHandlingFactory>
{
    [Theory]
    [InlineData("business", HttpStatusCode.UnprocessableEntity, "test.rule")]
    [InlineData("validation", HttpStatusCode.BadRequest, "test.invalid")]
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
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task A_domain_error_is_logged_with_its_final_status_not_as_500()
    {
        await factory.CreateClient().GetAsync("/test-errors/conflict");

        var completed = await RequestLogAsync("/test-errors/conflict");
        Assert.Equal("409", completed.Properties["StatusCode"].ToString());
        Assert.Equal(LogEventLevel.Information, completed.Level);
        Assert.Null(completed.Exception);
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

    [Fact]
    public async Task A_unique_index_violation_is_409_without_the_duplicate_value()
    {
        var response = await factory.CreateClient().PostAsync("/test-errors/duplicate", null);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("duplicate_value", JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
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
    }

    /// <summary>The "HTTP GET ... responded ..." event of the request logging; written right after the response.</summary>
    private static async Task<LogEvent> RequestLogAsync(string path)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var completed = ApiHostFactory.Logs.Events.LastOrDefault(e =>
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
