using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;
using LibraryApp.Api.ErrorHandling;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc;
using Serilog;

DotNetEnv.Env.TraversePath().NoClobber().Load();

var builder = WebApplication.CreateBuilder(args);

// JWT_SIGNING_KEY is the .env / docker compose name of Jwt:SigningKey.
if (Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") is { Length: > 0 } jwtSigningKey)
    builder.Configuration["Jwt:SigningKey"] = jwtSigningKey;

// Logging: Serilog to console and file (requirements document). Settings: appsettings "Serilog".
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Platform: database, unit of work, clock, id generator, health checks (Integration Owner).
builder.Services.AddPlatform(builder.Configuration, builder.Environment);

// Modules: each group uncomments or adds ONE line for its module here, in its own pull request. Nothing else.
builder.Services.AddLibraryIdentity();                          // Group 2 — Identity (+ JWT authentication later)
builder.Services.AddStaffModule(builder.Configuration);         // Group 2
builder.Services.AddCatalogModule(builder.Configuration);       // Group 1
// builder.Services.AddBorrowingModule(builder.Configuration);     // Group 3
// builder.Services.AddNotificationsModule(builder.Configuration); // Group 4
// builder.Services.AddReportingModule(builder.Configuration);     // Group 4

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())                                        // FluentValidation (SD-15)
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))   // enums as names (SD-11)
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>             // unreadable input: same shape
        new BadRequestObjectResult(ValidationFilter.ValidationProblem(context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray()), context.HttpContext))
        {
            ContentTypes = { "application/problem+json" },
        });

// Errors → ProblemDetails with code and traceId (NFR-07, SD-05).
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = GlobalExceptionHandler.AddTraceId);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // "Authorize" button (Group 2, MEM-02): paste the accessToken from POST /api/auth/login.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Name = "Authorization",
        Description = "Log in with POST /api/auth/login, then paste the accessToken here (without \"Bearer \")."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
}); 

var app = builder.Build();

// Fail at start, not on the first request, if the clock settings are wrong (Time:TimeZoneId).
_ = app.Services.GetRequiredService<IClock>();

// Outside the exception handler: the request log shows the status the client gets (409, 422 ...), not 500.
app.UseSerilogRequestLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();   // empty 401/403/404 answers get a ProblemDetails body as well

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Inside docker compose the API listens on plain HTTP only; compose sets UseHttpsRedirection=false.
if (app.Configuration.GetValue("UseHttpsRedirection", true))
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });                                  // the process is up
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });   // SQL Server answers

// Migrations and seed data at start (settings: Database:MigrateOnStartup, Seed:Enabled; seed only in Development).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

app.Run();

// Makes the entry point visible to WebApplicationFactory in LibraryApp.Api.IntegrationTests.
public partial class Program;
