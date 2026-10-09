using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class StaffManagementFactory : ApiHostFactory, IAsyncLifetime
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddScoped<IUserValidator<AppUser>, FailingRoleUserValidator>();
        services.AddScoped<IEventBus, FailingEnrollmentEventBus>();
    }

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        using var scope = Services.CreateScope();
        var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>().OfType<StaffSeeder>().Single();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(seeder.SeedAsync);
    }
}

/// <summary>For the failure test only: create succeeds; AddToRole's Identity update fails after the user was saved.</summary>
public sealed class FailingRoleUserValidator : IUserValidator<AppUser>
{
    private int _calls;
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
        Task.FromResult(user.Email!.StartsWith("profile-failure.") || (user.Email.StartsWith("identity-failure.") && ++_calls > 1)
            ? IdentityResult.Failed(new IdentityError { Code = "SimulatedIdentityFailure", Description = "Test failure" })
            : IdentityResult.Success);
}

public sealed class FailingEnrollmentEventBus(LibraryDbContext db, EventTypeRegistry registry) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class, IDomainEvent
    {
        await new OutboxEventBus(db, registry).PublishAsync(domainEvent, ct);
        if (db.ChangeTracker.Entries<StaffMember>().Any(x => x.Entity.CorporateEmail.StartsWith("event-failure.")))
            throw new InvalidOperationException("Simulated event failure after account and member creation.");
    }
}

public class StaffManagementEndpointTests(StaffManagementFactory factory) : IClassFixture<StaffManagementFactory>
{
    private const string Route = "/api/staff-members";
    private const string Password = "DemoPass1";

    private HttpClient Client(string? role = StaffRoles.Librarian)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                new LoginAccount(Guid.NewGuid(), "test.librarian@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active, [role]));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        return client;
    }

    private static EnrolStaffMemberRequest Request() => new(
        $"TEST-{Guid.NewGuid():N}", "Demo", "Employee", $"demo.{Guid.NewGuid():N}@ameriabank.am",
        StaffSeedIds.Departments.InformationTechnology, "Engineer", null, StaffSeedIds.Categories.Employee, Password);

    private async Task<StaffMemberDto> Enrol(EnrolStaffMemberRequest request, string role = StaffRoles.Librarian)
    {
        var response = await Client(role).PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StaffMemberDto>())!;
    }

    private static UpdateStaffMemberRequest UpdateRequest(StaffMemberDto member) => new(
        member.FirstName, member.LastName, member.CorporateEmail, member.DepartmentId,
        member.Position, member.LineManagerEmail, member.StaffCategoryId);

    private static async Task<JsonElement> AssertError(HttpResponseMessage response, HttpStatusCode status, string code, string? detail = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        if (detail is not null)
            Assert.Equal(detail, body.GetProperty("detail").GetString());
        return body;
    }

    [Fact]
    public async Task Enrol_creates_account_profile_and_outbox_event_and_returns_a_readable_location()
    {
        var request = Request() with { CorporateEmail = $"DEMO.{Guid.NewGuid():N}@AMERIABANK.AM" };
        var client = Client();
        var response = await client.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = (await response.Content.ReadFromJsonAsync<StaffMemberDto>())!;
        Assert.Equal(StaffRoles.StaffMember, member.Role);
        Assert.Equal("Active", member.Status);
        Assert.Equal("Information Technology", member.DepartmentName);
        Assert.Equal("Employee", member.StaffCategoryName);
        Assert.Equal(request.CorporateEmail.ToLowerInvariant(), member.CorporateEmail);
        Assert.Equal(DateTimeKind.Utc, member.JoinedLibraryAtUtc.Kind);
        Assert.Equal(member, await client.GetFromJsonAsync<StaffMemberDto>(response.Headers.Location));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(member.CorporateEmail);
        Assert.NotNull(user);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CheckPasswordAsync(user, Password));
        var messages = await db.OutboxMessages.AsNoTracking().ToListAsync();
        Assert.Contains(messages, x => JsonSerializer.Deserialize<StaffEnrolled>(x.Payload)?.StaffMemberId == member.Id);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gmail_returns_exact_enrollment_message()
    {
        var response = await Client().PostAsJsonAsync(Route, Request() with { CorporateEmail = "demo@gmail.com" });
        await AssertError(response, HttpStatusCode.BadRequest, "staff.enrol_email_not_corporate", "Only Ameriabank staff can be enrolled");
    }

    [Theory]
    [InlineData(true, "staff.employee_id_taken")]
    [InlineData(false, "staff.email_taken")]
    public async Task Duplicate_id_or_email_returns_409(bool duplicateId, string code)
    {
        var original = Request();
        await Enrol(original);
        var duplicate = duplicateId ? Request() with { EmployeeId = original.EmployeeId }
            : Request() with { CorporateEmail = original.CorporateEmail.ToUpperInvariant() };
        await AssertError(await Client().PostAsJsonAsync(Route, duplicate), HttpStatusCode.Conflict, code);
    }

    [Theory]
    [InlineData("Short1")]
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    public async Task Weak_password_returns_400(string password)
    {
        var response = await Client().PostAsJsonAsync(Route, Request() with { Password = password });
        var problem = await AssertError(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.NotEmpty(problem.GetProperty("errors").GetProperty("Password").EnumerateArray());
    }

    [Fact]
    public async Task Invalid_update_keeps_field_errors_and_does_not_change_profile()
    {
        var member = await Enrol(Request());
        var response = await Client().PutAsJsonAsync($"{Route}/{member.Id}", UpdateRequest(member) with
        {
            FirstName = "", DepartmentId = Guid.Empty, LineManagerEmail = "invalid"
        });
        var problem = await AssertError(response, HttpStatusCode.BadRequest, "validation_failed");
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("FirstName", out _));
        Assert.True(errors.TryGetProperty("DepartmentId", out _));
        Assert.True(errors.TryGetProperty("LineManagerEmail", out _));
        Assert.Equal(member, await Client().GetFromJsonAsync<StaffMemberDto>($"{Route}/{member.Id}"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("multiple")]
    [InlineData("unsupported")]
    public async Task Invalid_account_roles_return_the_same_clear_conflict_for_read_and_update(string scenario)
    {
        var member = await Enrol(Request());
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByEmailAsync(member.CorporateEmail))!;
            if (scenario == "multiple")
                Assert.True((await users.AddToRoleAsync(user, StaffRoles.Librarian)).Succeeded);
            else
            {
                Assert.True((await users.RemoveFromRoleAsync(user, StaffRoles.StaffMember)).Succeeded);
                if (scenario == "unsupported")
                {
                    var roleName = $"Unsupported-{Guid.NewGuid():N}";
                    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                    Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() })).Succeeded);
                    Assert.True((await users.AddToRoleAsync(user, roleName)).Succeeded);
                }
            }
        }

        const string code = "staff.account_role_invalid";
        const string detail = "The staff account must have exactly one supported role.";
        await AssertError(await Client().GetAsync($"{Route}/{member.Id}"), HttpStatusCode.Conflict, code, detail);
        var update = UpdateRequest(member) with { CorporateEmail = $"new.{Guid.NewGuid():N}@ameriabank.am", FirstName = "Changed" };
        await AssertError(await Client().PutAsJsonAsync($"{Route}/{member.Id}", update), HttpStatusCode.Conflict, code, detail);

        using var verification = factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var stored = await db.StaffMembers.AsNoTracking().SingleAsync(x => x.Id == member.Id);
        Assert.Equal(member.FirstName, stored.FirstName);
        Assert.Equal(member.CorporateEmail, stored.CorporateEmail);
        var account = await db.Users.AsNoTracking().SingleAsync(x => x.Id == stored.UserId);
        Assert.Equal(member.CorporateEmail, account.Email);
        Assert.Equal(member.CorporateEmail, account.UserName);
    }

    [Theory]
    [InlineData(StaffRoles.LibraryAdministrator)]
    [InlineData(StaffRoles.Librarian)]
    public async Task Librarian_cannot_assign_privileged_roles(string role) =>
        await AssertError(await Client().PostAsJsonAsync(Route, Request() with { Role = role }),
            HttpStatusCode.Forbidden, "staff.role_assignment_forbidden");

    [Theory]
    [InlineData(StaffRoles.LibraryAdministrator)]
    [InlineData(StaffRoles.Librarian)]
    public async Task Administrator_can_assign_privileged_roles(string role)
    {
        var member = await Enrol(Request() with { Role = role }, StaffRoles.LibraryAdministrator);
        Assert.Equal(role, member.Role);
        Assert.Equal(role, (await Client().GetFromJsonAsync<StaffMemberDto>($"{Route}/{member.Id}"))!.Role);
    }

    [Theory]
    [InlineData("post")]
    [InlineData("get")]
    [InlineData("put")]
    public async Task StaffMember_is_forbidden_and_anonymous_is_unauthorized(string method)
    {
        foreach (var role in new string?[] { StaffRoles.StaffMember, null })
        {
            var client = Client(role);
            var response = method switch
            {
                "post" => await client.PostAsJsonAsync(Route, Request()),
                "put" => await client.PutAsJsonAsync($"{Route}/{Guid.NewGuid()}", new { }),
                _ => await client.GetAsync($"{Route}/{Guid.NewGuid()}")
            };
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(true, "staff.department_not_found")]
    [InlineData(false, "staff.category_not_found")]
    public async Task Missing_reference_returns_400(bool department, string code)
    {
        var request = department ? Request() with { DepartmentId = Guid.NewGuid() }
            : Request() with { StaffCategoryId = Guid.NewGuid() };
        await AssertError(await Client().PostAsJsonAsync(Route, request), HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task Invalid_manager_email_or_role_returns_400()
    {
        foreach (var request in new[] { Request() with { LineManagerEmail = "invalid" }, Request() with { Role = "Admin" } })
            await AssertError(await Client().PostAsJsonAsync(Route, request), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Identity_failure_after_user_insert_rolls_back_account_member_role_and_event()
    {
        var request = Request() with { CorporateEmail = $"identity-failure.{Guid.NewGuid():N}@ameriabank.am" };
        using var beforeScope = factory.Services.CreateScope();
        var before = beforeScope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var accountCount = await before.Users.CountAsync();
        var roleCount = await before.UserRoles.CountAsync();
        var eventCount = await before.OutboxMessages.CountAsync();
        await AssertError(await Client().PostAsJsonAsync(Route, request), HttpStatusCode.BadRequest, "staff.account_write_failed");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.Email == request.CorporateEmail));
        Assert.False(await db.StaffMembers.AnyAsync(x => x.EmployeeId == request.EmployeeId));
        Assert.Equal(accountCount, await db.Users.CountAsync());
        Assert.Equal(roleCount, await db.UserRoles.CountAsync());
        Assert.Equal(eventCount, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Event_failure_rolls_back_account_member_and_staged_outbox_event()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var eventCount = await db.OutboxMessages.CountAsync();
        var roleCount = await db.UserRoles.CountAsync();
        var request = Request() with { CorporateEmail = $"event-failure.{Guid.NewGuid():N}@ameriabank.am" };
        await AssertError(await Client().PostAsJsonAsync(Route, request), HttpStatusCode.InternalServerError, "internal_error");
        Assert.False(await db.Users.AnyAsync(x => x.Email == request.CorporateEmail));
        Assert.False(await db.StaffMembers.AnyAsync(x => x.EmployeeId == request.EmployeeId));
        Assert.Equal(eventCount, await db.OutboxMessages.CountAsync());
        Assert.Equal(roleCount, await db.UserRoles.CountAsync());
    }

    [Fact]
    public async Task Parallel_duplicate_employee_enrollment_leaves_one_account_and_one_member()
    {
        var first = Request();
        var second = Request() with { EmployeeId = first.EmployeeId };
        var results = await Task.WhenAll(Client().PostAsJsonAsync(Route, first), Client().PostAsJsonAsync(Route, second));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.Equal(1, await db.StaffMembers.CountAsync(x => x.EmployeeId == first.EmployeeId));
        Assert.Equal(1, await db.Users.CountAsync(x => x.Email == first.CorporateEmail || x.Email == second.CorporateEmail));
    }

    [Fact]
    public async Task Failed_identity_email_update_preserves_both_profile_and_login_email()
    {
        var member = await Enrol(Request());
        var update = UpdateRequest(member) with { CorporateEmail = $"profile-failure.{Guid.NewGuid():N}@ameriabank.am" };
        await AssertError(await Client().PutAsJsonAsync($"{Route}/{member.Id}", update), HttpStatusCode.BadRequest, "staff.account_write_failed");
        Assert.Equal(member, await Client().GetFromJsonAsync<StaffMemberDto>($"{Route}/{member.Id}"));
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.NotNull(await users.FindByEmailAsync(member.CorporateEmail));
        Assert.Null(await users.FindByEmailAsync(update.CorporateEmail));
    }

    [Fact]
    public async Task Swagger_documents_staff_routes_responses_and_summary()
    {
        var spec = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = spec.GetProperty("paths");
        var post = paths.GetProperty(Route).GetProperty("post");
        Assert.Contains("Enrolls an employee", post.GetProperty("summary").GetString());
        Assert.True(post.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(paths.GetProperty(Route + "/{id}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty(Route + "/{id}").TryGetProperty("put", out _));
        var example = spec.GetProperty("components").GetProperty("schemas")
            .GetProperty(nameof(EnrolStaffMemberRequest)).GetProperty("example");
        Assert.Equal("demo.employee@ameriabank.am", example.GetProperty("corporateEmail").GetString());
        Assert.Equal(StaffSeedIds.Departments.InformationTechnology, example.GetProperty("departmentId").GetGuid());
        Assert.Equal(StaffSeedIds.Categories.Employee, example.GetProperty("staffCategoryId").GetGuid());
    }

    [Fact]
    public async Task Update_changes_profile_and_login_email_but_preserves_employee_id_role_and_joined_date()
    {
        var member = await Enrol(Request());
        var update = UpdateRequest(member) with
        {
            FirstName = "Updated", LastName = "Name", Position = "Senior Engineer",
            CorporateEmail = $"updated.{Guid.NewGuid():N}@ameriabank.am", LineManagerEmail = "manager@example.org",
            DepartmentId = StaffSeedIds.Departments.RiskManagement, StaffCategoryId = StaffSeedIds.Categories.ManagerSenior
        };
        var response = await Client().PutAsJsonAsync($"{Route}/{member.Id}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changed = (await response.Content.ReadFromJsonAsync<StaffMemberDto>())!;
        Assert.Equal(member.EmployeeId, changed.EmployeeId);
        Assert.Equal(member.Role, changed.Role);
        Assert.Equal(member.JoinedLibraryAtUtc, changed.JoinedLibraryAtUtc);
        Assert.Equal(update.FirstName, changed.FirstName);
        Assert.Equal(update.LastName, changed.LastName);
        Assert.Equal(update.Position, changed.Position);
        Assert.Equal(update.LineManagerEmail, changed.LineManagerEmail);
        Assert.Equal("Risk Management", changed.DepartmentName);
        Assert.Equal(update.StaffCategoryId, changed.StaffCategoryId);
        Assert.Equal(changed, await Client().GetFromJsonAsync<StaffMemberDto>($"{Route}/{member.Id}"));

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Null(await users.FindByEmailAsync(member.CorporateEmail));
        var user = await users.FindByEmailAsync(update.CorporateEmail);
        Assert.NotNull(user);
        Assert.Equal(update.CorporateEmail, user.UserName);
        Assert.Equal(update.CorporateEmail.ToUpperInvariant(), user.NormalizedUserName);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(update.CorporateEmail, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Update_rejects_duplicate_and_noncorporate_email_without_changing_profile()
    {
        var member = await Enrol(Request());
        var other = await Enrol(Request());
        await AssertError(await Client().PutAsJsonAsync($"{Route}/{member.Id}",
            UpdateRequest(member) with { CorporateEmail = other.CorporateEmail }), HttpStatusCode.Conflict, "staff.email_taken");
        await AssertError(await Client().PutAsJsonAsync($"{Route}/{member.Id}",
            UpdateRequest(member) with { CorporateEmail = "demo@gmail.com" }), HttpStatusCode.BadRequest, "staff.enrol_email_not_corporate");
        Assert.Equal(member, await Client().GetFromJsonAsync<StaffMemberDto>($"{Route}/{member.Id}"));
    }

    [Fact]
    public async Task Update_deactivated_member_returns_409()
    {
        var id = StaffSeedIds.StaffMembers.HaykManukyan;
        var member = (await Client().GetFromJsonAsync<StaffMemberDto>($"{Route}/{id}"))!;
        await AssertError(await Client().PutAsJsonAsync($"{Route}/{id}", UpdateRequest(member)),
            HttpStatusCode.Conflict, "staff.deactivated_profile_locked", "A deactivated staff member's profile cannot be updated.");
    }

    [Fact]
    public async Task Missing_member_returns_404_for_read_and_update()
    {
        await AssertError(await Client().GetAsync($"{Route}/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "staff.not_found");
        var r = Request();
        await AssertError(await Client().PutAsJsonAsync($"{Route}/{Guid.NewGuid()}", new UpdateStaffMemberRequest(
            r.FirstName, r.LastName, r.CorporateEmail, r.DepartmentId, r.Position, r.LineManagerEmail, r.StaffCategoryId)),
            HttpStatusCode.NotFound, "staff.not_found");
    }
}
