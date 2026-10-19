using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
using Members =
    LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// Tests the lost-copy endpoint through HTTP, using real SQL transactions,
/// Catalog, Staff and the outbox.
/// </summary>
public sealed class LoanLostEndpointTests : IAsyncLifetime
{
    private readonly LostHost _host = new();

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

    private static readonly DateTime Now =
        new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid CopyId =
        CatalogSeedIds.Copy(57, 1);

    private static readonly Guid BookId =
        CatalogSeedIds.Book(57);

    private static readonly Guid StaffId =
        Members.AnnaPetrosyan;

    public Task InitializeAsync() => _host.InitializeAsync();

    public Task DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData(StaffRoles.Librarian, false)]
    [InlineData(StaffRoles.LibraryAdministrator, false)]
    [InlineData(StaffRoles.Librarian, true)]
    public async Task Loss_commits_loan_copy_penalty_and_status_events(
        string role,
        bool overdue)
    {
        Guid id = await SeedAsync(
            overdueToday: overdue,
            queue: true);

        using var client = Client(role);
        using var response = await LostAsync(client, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = (await response.Content
            .ReadFromJsonAsync<DeclareLostResponse>(Json))!;

        Assert.Equal(id, result.LoanId);
        Assert.Equal(LoanStatus.Lost, result.Status);
        Assert.Equal(Now, result.DeclaredAtUtc);
        Assert.Equal(10, result.PenaltyPointsApplied);

        Loan loan = await ReadLoanAsync(id);

        Assert.Equal(LoanStatus.Lost, loan.Status);
        Assert.Equal(Now, loan.ReturnedAtUtc);

        Assert.Equal(
            BookCopyStatus.Lost,
            await CopyStatusAsync());

        Assert.Equal(
            StaffMemberStatus.Suspended,
            await StaffStatusAsync());

        var penalty = Assert.Single(
            await Db(db => db.PenaltyRecords
                .Where(record => record.LoanId == id)
                .ToListAsync()));

        Assert.Equal(10, penalty.Points);
        Assert.Equal(Now.AddDays(90), penalty.ExpiresAtUtc);
        Assert.Equal(StaffId, penalty.StaffMemberId);

        var applied = Assert.Single(
            await EventsAsync<PenaltyApplied>(),
            domainEvent => domainEvent.LoanId == id);

        Assert.Equal(10, applied.Points);
        Assert.Equal(10, applied.ActivePointsTotal);

        var changed = Assert.Single(
            await EventsAsync<StaffStatusChanged>(),
            domainEvent => domainEvent.StaffMemberId == StaffId);

        Assert.Equal(
            StaffMemberStatus.Active,
            changed.OldStatus);

        Assert.Equal(
            StaffMemberStatus.Suspended,
            changed.NewStatus);

        Assert.Equal(
            Now.AddDays(30),
            changed.EffectiveUntilUtc);

        Assert.All(
            await ReadQueueAsync(),
            reservation =>
            {
                Assert.Equal(
                    ReservationStatus.Waiting,
                    reservation.Status);

                Assert.Null(reservation.CopyId);
            });

        Assert.Empty(await EventsAsync<ReservationReady>());
        Assert.Empty(await EventsAsync<LoanReturned>());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    public async Task Loss_requires_library_staff(
        string? role,
        HttpStatusCode expected)
    {
        Guid id = await SeedAsync();

        using var client = Client(role);
        using var response = await LostAsync(client, id);

        Assert.Equal(expected, response.StatusCode);

        Assert.Equal(
            LoanStatus.Active,
            (await ReadLoanAsync(id)).Status);

        Assert.Equal(
            BookCopyStatus.OnLoan,
            await CopyStatusAsync());

        Assert.Equal(
            0,
            await Db(db => db.PenaltyRecords
                .CountAsync(record => record.LoanId == id)));
    }

    [Fact]
    public async Task Unknown_loan_returns_404()
    {
        using var client = Client();

        using var response = await LostAsync(
            client,
            Guid.NewGuid());

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.UnprocessableEntity)]
    [InlineData(true, false, HttpStatusCode.OK)]
    [InlineData(false, true, HttpStatusCode.OK)]
    public async Task Early_loss_setting_is_observed(
        bool late,
        bool alreadyOverdue,
        HttpStatusCode expected)
    {
        Guid id = await SeedAsync(
            late: late,
            overdueToday: alreadyOverdue);

        _host.Services
            .GetRequiredService<IOptions<BorrowingOptions>>()
            .Value.Loans.AllowLostBeforeDue = false;

        using var client = Client();
        using var response = await LostAsync(client, id);

        Assert.Equal(expected, response.StatusCode);

        if (expected != HttpStatusCode.OK)
        {
            var body = await response.Content
                .ReadFromJsonAsync<JsonElement>();

            Assert.Equal(
                BorrowingErrors.LoanNotPastDue.Code,
                body.GetProperty("code").GetString());

            Assert.Equal(
                LoanStatus.Active,
                (await ReadLoanAsync(id)).Status);

            Assert.Equal(
                BookCopyStatus.OnLoan,
                await CopyStatusAsync());

            Assert.Equal(
                0,
                await Db(db => db.PenaltyRecords
                    .CountAsync(record => record.LoanId == id)));
        }
    }

    [Fact]
    public async Task Repeated_loss_and_return_of_lost_loan_are_rejected_without_new_events()
    {
        Guid id = await SeedAsync();

        using var client = Client();
        using var first = await LostAsync(client, id);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        int eventsBefore = await Db(
            db => db.OutboxMessages.CountAsync());

        using var repeated = await LostAsync(client, id);

        using var returned = await client.PostAsJsonAsync(
            $"/api/loans/{id}/return",
            new ReturnLoanRequest(false, null));

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            repeated.StatusCode);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            returned.StatusCode);

        Assert.Equal(
            1,
            await Db(db => db.PenaltyRecords
                .CountAsync(record => record.LoanId == id)));

        Assert.Equal(
            eventsBefore,
            await Db(db => db.OutboxMessages.CountAsync()));

        Assert.Equal(
            BookCopyStatus.Lost,
            await CopyStatusAsync());

        // Catalog must also reject restoring the lost copy to Available.
        using var scope = _host.Services.CreateScope();

        var catalog = scope.ServiceProvider
            .GetRequiredService<ICatalogService>();

        await Assert.ThrowsAsync<DomainException>(
            () => catalog.SetCopyStatusAsync(
                CopyId,
                BookCopyStatus.Available));

        Assert.Equal(
            BookCopyStatus.Lost,
            await CopyStatusAsync());
    }

    [Fact]
    public async Task Returned_loan_cannot_be_declared_lost()
    {
        Guid id = await SeedAsync();

        using var client = Client();

        using var returned = await client.PostAsJsonAsync(
            $"/api/loans/{id}/return",
            new ReturnLoanRequest(false, null));

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        using var lost = await LostAsync(client, id);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            lost.StatusCode);

        Assert.Equal(
            BookCopyStatus.Available,
            await CopyStatusAsync());

        Assert.Equal(
            0,
            await Db(db => db.PenaltyRecords
                .CountAsync(record => record.LoanId == id)));
    }

    [Fact]
    public async Task Already_suspended_member_gets_penalty_without_duplicate_status_event()
    {
        Guid id = await SeedAsync();

        await Db(async db =>
        {
            var staff = await db.StaffMembers
                .SingleAsync(member => member.Id == StaffId);

            staff.ChangeStatus(StaffMemberStatus.Suspended);

            return await db.SaveChangesAsync();
        });

        using var client = Client();
        using var response = await LostAsync(client, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Single(
            await EventsAsync<PenaltyApplied>(),
            domainEvent => domainEvent.LoanId == id);

        Assert.DoesNotContain(
            await EventsAsync<StaffStatusChanged>(),
            domainEvent => domainEvent.StaffMemberId == StaffId);
    }

    [Fact]
    public async Task Restricted_member_crossing_the_suspended_threshold_is_suspended()
    {
        Guid id = await SeedAsync(priorPoints: 5);

        using var client = Client();
        using var response = await LostAsync(client, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(
            StaffMemberStatus.Suspended,
            await StaffStatusAsync());

        var applied = Assert.Single(
            await EventsAsync<PenaltyApplied>(),
            domainEvent => domainEvent.LoanId == id);

        Assert.Equal(10, applied.Points);
        Assert.Equal(15, applied.ActivePointsTotal);

        var changed = Assert.Single(
            await EventsAsync<StaffStatusChanged>(),
            domainEvent => domainEvent.StaffMemberId == StaffId);

        Assert.Equal(StaffMemberStatus.Restricted, changed.OldStatus);
        Assert.Equal(StaffMemberStatus.Suspended, changed.NewStatus);
        Assert.Equal(Now.AddDays(30), changed.EffectiveUntilUtc);
    }

    [Fact]
    public async Task Failure_after_sql_writes_rolls_back_all_changes()
    {
        Guid id = await SeedAsync(queue: true);

        int eventsBefore = await Db(
            db => db.OutboxMessages.CountAsync());

        _host.FailAfterSave = true;

        using var client = Client();
        using var response = await LostAsync(client, id);

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        Assert.True(_host.SavedBeforeFailure);

        Loan loan = await ReadLoanAsync(id);

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Null(loan.ReturnedAtUtc);

        Assert.Equal(
            BookCopyStatus.OnLoan,
            await CopyStatusAsync());

        Assert.Equal(
            StaffMemberStatus.Active,
            await StaffStatusAsync());

        Assert.Equal(
            0,
            await Db(db => db.PenaltyRecords
                .CountAsync(record => record.LoanId == id)));

        Assert.Equal(
            eventsBefore,
            await Db(db => db.OutboxMessages.CountAsync()));
    }

    [Fact]
    public async Task Concurrent_loss_requests_apply_points_once()
    {
        Guid id = await SeedAsync();

        using var first = Client();
        using var second = Client();

        var responses = await Task.WhenAll(
            LostAsync(first, id),
            LostAsync(second, id));

        try
        {
            Assert.Equal(
                new[]
                {
                    HttpStatusCode.OK,
                    HttpStatusCode.UnprocessableEntity
                },
                responses
                    .Select(response => response.StatusCode)
                    .OrderBy(status => (int)status));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        Assert.Equal(
            1,
            await Db(db => db.PenaltyRecords
                .CountAsync(record => record.LoanId == id)));

        Assert.Single(
            await EventsAsync<PenaltyApplied>(),
            domainEvent => domainEvent.LoanId == id);

        Assert.Single(
            await EventsAsync<StaffStatusChanged>(),
            domainEvent => domainEvent.StaffMemberId == StaffId);
    }

    private static Task<HttpResponseMessage> LostAsync(
        HttpClient client,
        Guid loanId) =>
        client.PostAsync(
            $"/api/loans/{loanId}/lost",
            null);

    private HttpClient Client(
        string? role = StaffRoles.Librarian)
    {
        HttpClient client = _host.CreateClient();

        if (role is not null)
        {
            var account = new LoginAccount(
                Guid.NewGuid(),
                "lost-test@ameriabank.am",
                Guid.NewGuid(),
                StaffMemberStatus.Active,
                [role]);

            // Business operations use the fixed test date.
            // JWT tokens use the actual current time so they remain valid.
            var issuer = new JwtTokenIssuer(
                _host.Services
                    .GetRequiredService<IOptions<JwtOptions>>(),
                new LostClock(() => DateTime.UtcNow));

            string token = issuer.Issue(account).Token;

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private async Task<T> Db<T>(
        Func<LibraryDbContext, Task<T>> action)
    {
        using var scope = _host.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<LibraryDbContext>();

        return await action(db);
    }

    /// <summary>
    /// Creates the initial database state.
    /// The lost operation itself is performed through HTTP.
    /// </summary>
    private Task<Guid> SeedAsync(
        bool late = false,
        int priorPoints = 0,
        bool queue = false,
        bool overdueToday = false) =>
        Db(async db =>
        {
            var staff = await db.StaffMembers
                .SingleAsync(member => member.Id == StaffId);

            staff.ChangeStatus(
                priorPoints >= 5
                    ? StaffMemberStatus.Restricted
                    : StaffMemberStatus.Active);

            var copy = await db.BookCopies
                .SingleAsync(item => item.Id == CopyId);

            copy.ChangeStatus(BookCopyStatus.OnLoan);

            DateTime dueAtUtc = late
                ? new DateTime(
                    2026, 10, 16, 12, 0, 0, DateTimeKind.Utc)
                : overdueToday
                    ? Now.AddHours(-2)
                    : Now.AddDays(1);

            Loan loan = Loan.Issue(
                Guid.NewGuid(),
                CopyId,
                StaffId,
                dueAtUtc.AddDays(-14),
                14);

            if (overdueToday)
                loan.MarkOverdue(Now);

            db.Loans.Add(loan);

            if (priorPoints > 0)
            {
                db.PenaltyRecords.Add(
                    PenaltyRecord.Create(
                        Guid.NewGuid(),
                        StaffId,
                        loan.Id,
                        priorPoints,
                        "Existing penalty",
                        Now.AddDays(-1),
                        Now.AddDays(89)));
            }

            if (queue)
            {
                db.Reservations.Add(
                    Reservation.Create(
                        Guid.NewGuid(),
                        BookId,
                        Members.MariamAvetisyan,
                        Now.AddDays(-2),
                        1));

                db.Reservations.Add(
                    Reservation.Create(
                        Guid.NewGuid(),
                        BookId,
                        Members.DavitHakobyan,
                        Now.AddDays(-1),
                        2));
            }

            await db.SaveChangesAsync();

            return loan.Id;
        });

    private Task<Loan> ReadLoanAsync(Guid loanId) =>
        Db(db => db.Loans
            .AsNoTracking()
            .SingleAsync(loan => loan.Id == loanId));

    private Task<BookCopyStatus> CopyStatusAsync() =>
        Db(db => db.BookCopies
            .Where(copy => copy.Id == CopyId)
            .Select(copy => copy.Status)
            .SingleAsync());

    private Task<StaffMemberStatus> StaffStatusAsync() =>
        Db(db => db.StaffMembers
            .Where(member => member.Id == StaffId)
            .Select(member => member.Status)
            .SingleAsync());

    private Task<List<Reservation>> ReadQueueAsync() =>
        Db(db => db.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.BookId == BookId)
            .OrderBy(reservation => reservation.QueuePosition)
            .ToListAsync());

    private Task<List<T>> EventsAsync<T>()
        where T : class, IDomainEvent =>
        Db(async db =>
        {
            List<OutboxMessage> messages = await db.OutboxMessages
                .AsNoTracking()
                .Where(message => message.Type == typeof(T).Name)
                .ToListAsync();

            return messages
                .Select(message =>
                    JsonSerializer.Deserialize<T>(
                        message.Payload,
                        Json)!)
                .ToList();
        });

    /// <summary>
    /// Starts the real API with a fixed business clock,
    /// no Borrowing seed and a test outbox wrapper.
    /// </summary>
    private sealed class LostHost : ApiHostFactory
    {
        public bool FailAfterSave { get; set; }

        public bool SavedBeforeFailure { get; set; }

        protected override void ConfigureTestServices(
            IServiceCollection services)
        {
            ServiceDescriptor borrowingSeed = services.Single(
                descriptor =>
                    descriptor.ServiceType == typeof(IModuleSeeder)
                    && descriptor.ImplementationType ==
                    typeof(BorrowingSeeder));

            services.Remove(borrowingSeed);

            services.RemoveAll<IClock>();

            services.AddSingleton<IClock>(
                new LostClock(() => Now));

            services.RemoveAll<IEventBus>();

            services.AddScoped<IEventBus>(provider =>
                new TestOutboxBus(
                    provider.GetRequiredService<LibraryDbContext>(),
                    provider.GetRequiredService<EventTypeRegistry>(),
                    this));
        }
    }

    /// <summary>
    /// Delegates to the real outbox.
    /// The rollback test enables a deliberate failure after SQL writes.
    /// </summary>
    private sealed class TestOutboxBus(
        LibraryDbContext db,
        EventTypeRegistry registry,
        LostHost host) : IEventBus
    {
        private readonly OutboxEventBus _inner =
            new(db, registry);

        public async Task PublishAsync<TEvent>(
            TEvent domainEvent,
            CancellationToken ct = default)
            where TEvent : class, IDomainEvent
        {
            await _inner.PublishAsync(domainEvent, ct);

            if (host.FailAfterSave
                && domainEvent is StaffStatusChanged)
            {
                // Flush staged changes while the transaction is still open.
                // The deliberately thrown exception must roll them back.
                await db.SaveChangesAsync(ct);

                host.SavedBeforeFailure = true;

                throw new InvalidOperationException(
                    "Deliberate lost integration-test failure.");
            }
        }
    }

    private sealed class LostClock(
        Func<DateTime> now) : IClock
    {
        private static readonly TimeZoneInfo Zone =
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan");

        public DateTime UtcNow => now();

        public DateOnly LocalToday => ToLocalDate(now());

        public DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);

        public DateOnly ToLocalDate(DateTime utc) =>
            DateOnly.FromDateTime(ToLocal(utc));

        public DateTime StartOfLocalDayUtc(DateOnly localDate) =>
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(
                    localDate.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Unspecified),
                Zone);

        public DateTime EndOfLocalDayUtc(DateOnly localDate) =>
            StartOfLocalDayUtc(
                localDate.AddDays(1)).AddTicks(-1);
    }
}