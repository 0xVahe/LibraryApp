using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class StaffDeactivationFactory : ApiHostFactory, IAsyncLifetime
{
    public DeactivationLoanQueries Loans { get; } = new();
    public Guid BookId { get; } = CatalogSeedIds.Book(15);
    public Guid CopyId { get; } = Guid.NewGuid();
    public ConcurrentDictionary<Guid, string> OriginalStamps { get; } = new();

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(ct =>
        {
            var copy = new BookCopy(CopyId, BookId, "BK-DEMO-000123", BookCopyCondition.Good, null);
            copy.ChangeStatus(BookCopyStatus.OnLoan);
            db.BookCopies.Add(copy);
            return Task.CompletedTask;
        });
    }
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<IBorrowingReadService>(Loans);
        services.AddScoped<IApplicationLock>(provider => new ObservedStaffLock(
            new SqlServerApplicationLock(provider.GetRequiredService<LibraryDbContext>()), Loans));
        services.AddScoped<IEventBus, DeactivationFailureEventBus>();
        services.AddScoped<IUserValidator<AppUser>, DeactivationFailureUserValidator>();
    }
}

public sealed class DeactivationLoanQueries : IBorrowingReadService
{
    public ConcurrentDictionary<Guid, IReadOnlyList<OpenLoanInfo>> Items { get; } = new();
    public ConcurrentDictionary<string, TaskCompletionSource> LockRequests { get; } = new();
    public Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default) =>
        Task.FromResult(Items.GetValueOrDefault(staffMemberId) ?? (IReadOnlyList<OpenLoanInfo>)[]);
}

public sealed class ObservedStaffLock(IApplicationLock inner, DeactivationLoanQueries queries) : IApplicationLock
{
    public Task AcquireAsync(string resource, CancellationToken ct = default)
    {
        if (queries.LockRequests.TryGetValue(resource, out var requested)) requested.TrySetResult();
        return inner.AcquireAsync(resource, ct);
    }
}

public sealed class DeactivationFailureEventBus(LibraryDbContext db, EventTypeRegistry registry) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class, IDomainEvent
    {
        await new OutboxEventBus(db, registry).PublishAsync(domainEvent, ct);
        if (domainEvent is StaffStatusChanged { Reason: "Simulate event failure" })
            throw new InvalidOperationException("Simulated outbox failure.");
    }
}

public sealed class DeactivationFailureUserValidator : IUserValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
        Task.FromResult(user.LockoutEnd == DateTimeOffset.MaxValue && user.Email!.StartsWith("lockout-failure.")
            ? IdentityResult.Failed(new IdentityError { Code = "SimulatedFailure", Description = "Test failure" })
            : IdentityResult.Success);
}

public class StaffDeactivationEndpointTests(StaffDeactivationFactory factory) : IClassFixture<StaffDeactivationFactory>
{
    private const string Password = "DemoPass1";
    private const string Reason = "Left the bank";
    private static string Route(Guid id) => $"/api/staff-members/{id}/deactivate";

    private HttpClient Client(string? role = StaffRoles.Librarian)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                new LoginAccount(Guid.NewGuid(), "demo@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active, [role]));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        return client;
    }

    private async Task<StaffMemberDto> Enrol(string prefix = "deactivation")
    {
        var request = new EnrolStaffMemberRequest($"TEST-{Guid.NewGuid():N}", "Demo", "Employee",
            $"{prefix}.{Guid.NewGuid():N}@ameriabank.am", StaffSeedIds.Departments.InformationTechnology,
            "Engineer", null, StaffSeedIds.Categories.Employee, Password);
        var response = await Client().PostAsJsonAsync("/api/staff-members", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = (await response.Content.ReadFromJsonAsync<StaffMemberDto>())!;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        factory.OriginalStamps[member.Id] = (await db.Users.SingleAsync(x => x.Email == member.CorporateEmail)).SecurityStamp!;
        return member;
    }

    private async Task<JsonElement> Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        return body;
    }

    private async Task AssertStoredState(StaffMemberDto member, bool deactivated)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var stored = await db.StaffMembers.AsNoTracking().SingleAsync(x => x.Id == member.Id);
        Assert.Equal(deactivated ? StaffMemberStatus.Deactivated : StaffMemberStatus.Active, stored.Status);
        var account = await db.Users.AsNoTracking().SingleAsync(x => x.Id == stored.UserId);
        Assert.Equal(deactivated ? DateTimeOffset.MaxValue : null, account.LockoutEnd);
        if (deactivated) Assert.True(account.LockoutEnabled);
        if (deactivated) Assert.NotEqual(factory.OriginalStamps[member.Id], account.SecurityStamp);
        else Assert.Equal(factory.OriginalStamps[member.Id], account.SecurityStamp);
        var events = (await db.Set<OutboxMessage>().Where(x => x.Type == nameof(StaffStatusChanged)).ToListAsync())
            .Select(x => JsonSerializer.Deserialize<StaffStatusChanged>(x.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .Where(x => x.StaffMemberId == member.Id).ToList();
        Assert.Equal(deactivated ? 1 : 0, events.Count);
        if (deactivated)
        {
            var changed = Assert.Single(events);
            Assert.Equal(Reason, changed.Reason);
            Assert.Equal(StaffMemberStatus.Active, changed.OldStatus);
            Assert.Equal(StaffMemberStatus.Deactivated, changed.NewStatus);
            Assert.Null(changed.EffectiveUntilUtc);
        }
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Success_atomically_deactivates_locks_account_and_saves_event(string role)
    {
        var member = await Enrol();
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client(role).PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest($"  {Reason}  "))).StatusCode);
        await AssertStoredState(member, true);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(member.CorporateEmail, Password));
        await Error(login, HttpStatusCode.Forbidden, "staff.account_deactivated");
        await Error(await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(member.CorporateEmail, "WrongPass1")),
            HttpStatusCode.Unauthorized, "staff.invalid_credentials");
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    public async Task Unauthorized_callers_cannot_deactivate(string? role, HttpStatusCode expected)
    {
        var member = await Enrol();
        Assert.Equal(expected, (await Client(role).PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(Reason))).StatusCode);
        await AssertStoredState(member, false);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Invalid_reason_returns_field_errors_without_writes(string? reason)
    {
        var member = await Enrol();
        var problem = await Error(await Client().PostAsJsonAsync(Route(member.Id), new { reason }),
            HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("Reason", out _));
        await AssertStoredState(member, false);
    }

    [Fact]
    public async Task Missing_and_already_deactivated_members_return_documented_errors()
    {
        await Error(await Client().PostAsJsonAsync(Route(Guid.NewGuid()), new DeactivateStaffMemberRequest(Reason)),
            HttpStatusCode.NotFound, "staff.not_found");
        var member = await Enrol();
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(Reason))).StatusCode);
        await Error(await Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(Reason)),
            HttpStatusCode.UnprocessableEntity, "staff.already_deactivated");
        await AssertStoredState(member, true);
    }

    [Theory]
    [InlineData(LoanStatus.Active)]
    [InlineData(LoanStatus.Overdue)]
    public async Task Outstanding_loans_return_titles_and_barcodes_in_409_without_writes(LoanStatus status)
    {
        var member = await Enrol();
        factory.Loans.Items[member.Id] = [new(Guid.NewGuid(), member.Id,
            factory.CopyId,
            factory.BookId,
            new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), status)];
        var problem = await Error(await Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(Reason)),
            HttpStatusCode.Conflict, "staff.has_active_loans");
        var book = Assert.Single(problem.GetProperty("outstandingBooks").EnumerateArray());
        Assert.Equal("Clean Code", book.GetProperty("title").GetString());
        Assert.Equal("BK-DEMO-000123", book.GetProperty("barcode").GetString());
        await AssertStoredState(member, false);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(member.CorporateEmail, Password))).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identity_or_event_failure_rolls_back_profile_account_and_outbox(bool identityFailure)
    {
        var member = await Enrol(identityFailure ? "lockout-failure" : "event-failure");
        await Error(await Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(
            identityFailure ? Reason : "Simulate event failure")),
            identityFailure ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError,
            identityFailure ? "staff.account_write_failed" : "internal_error");
        await AssertStoredState(member, false);
    }

    [Fact]
    public async Task Parallel_deactivations_commit_only_one_status_event()
    {
        var member = await Enrol();
        var responses = await Task.WhenAll(Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(Reason)),
            Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(Reason)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        await Error(Assert.Single(responses, x => x.StatusCode != HttpStatusCode.NoContent),
            HttpStatusCode.UnprocessableEntity, "staff.already_deactivated");
        await AssertStoredState(member, true);
    }

    [Fact]
    public async Task Deactivation_waits_for_issuance_lock_and_reads_the_new_open_loan_after_release()
    {
        var member = await Enrol();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IApplicationLock>().AcquireAsync(BorrowingLocks.Staff(member.Id));
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Loans.LockRequests[BorrowingLocks.Staff(member.Id)] = attempted;
        var request = Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(Reason));
        // The request must not finish while issuance holds the same transaction-owned lock.
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(request.IsCompleted);
        factory.Loans.Items[member.Id] = [new(Guid.NewGuid(), member.Id,
            factory.CopyId,
            factory.BookId,
            new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), LoanStatus.Active)];
        await transaction.CommitAsync();
        await Error(await request, HttpStatusCode.Conflict, "staff.has_active_loans");
        await AssertStoredState(member, false);
    }

    [Fact]
    public void Application_uses_the_real_catalog_provider()
    {
        using var scope = factory.Services.CreateScope();
        Assert.IsType<CatalogService>(scope.ServiceProvider.GetRequiredService<ICatalogService>());
    }

    [Theory]
    [InlineData(1000, HttpStatusCode.NoContent)]
    [InlineData(1001, HttpStatusCode.BadRequest)]
    public async Task Reason_length_is_checked_after_trimming(int length, HttpStatusCode expected)
    {
        var member = await Enrol();
        var response = await Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest("  " + new string('x', length) + "  "));
        Assert.Equal(expected, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().StaffMembers.SingleAsync(x => x.Id == member.Id);
        Assert.Equal(expected == HttpStatusCode.NoContent ? StaffMemberStatus.Deactivated : StaffMemberStatus.Active, stored.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unresolvable_catalog_reference_logs_loan_id_and_preserves_account(bool missingBook)
    {
        var member = await Enrol();
        var loan = new OpenLoanInfo(Guid.NewGuid(), member.Id,
            missingBook ? factory.CopyId : Guid.NewGuid(), missingBook ? Guid.NewGuid() : factory.BookId,
            new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), LoanStatus.Active);
        factory.Loans.Items[member.Id] = [loan];
        await Error(await Client().PostAsJsonAsync(Route(member.Id), new DeactivateStaffMemberRequest(Reason)),
            HttpStatusCode.InternalServerError, "internal_error");
        Assert.Contains(ApiHostFactory.Logs.Events, log => log.Properties.TryGetValue("LoanId", out var value)
            && value is ScalarValue { Value: Guid loanId } && loanId == loan.LoanId);
        await AssertStoredState(member, false);
    }

    [Fact]
    public async Task Swagger_documents_success_and_error_responses()
    {
        var swagger = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var operation = swagger.GetProperty("paths").GetProperty("/api/staff-members/{id}/deactivate").GetProperty("post");
        foreach (var status in new[] { "204", "400", "401", "403", "404", "409", "422" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        Assert.Contains("outstandingBooks", operation.GetProperty("description").GetString());
    }
}
