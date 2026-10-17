using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Borrowing.Dto;
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
/// Tests loan returns through the real API and SQL Server database.
/// Each test case owns its own host and database.
/// </summary>
public sealed class LoanReturnEndpointTests : IAsyncLifetime
{
    private readonly ReturnHost _host = new();

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

    // Tuesday, 16:00 in Yerevan.
    private static readonly DateTime Now =
        new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid CopyId = CatalogSeedIds.Copy(57, 1);
    private static readonly Guid BookId = CatalogSeedIds.Book(57);
    private static readonly Guid StaffId = Members.AnnaPetrosyan;

    public Task InitializeAsync() => _host.InitializeAsync();

    public Task DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task On_time_return_saves_note_and_releases_copy(
        string role)
    {
        Guid loanId = await SeedAsync();

        using var client = Client(role);
        using var response = await ReturnAsync(
            client,
            loanId,
            note: "  Checked cover.  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result =
            (await response.Content.ReadFromJsonAsync<ReturnLoanResponse>(
                Json))!;

        Assert.Equal(loanId, result.LoanId);
        Assert.Equal(LoanStatus.Returned, result.Status);
        Assert.Equal(Now, result.ReturnedAtUtc);
        Assert.Equal(0, result.PenaltyPointsApplied);
        Assert.Equal(0, result.OverdueWorkingDays);

        Loan loan = await ReadLoanAsync(loanId);

        Assert.Equal(LoanStatus.Returned, loan.Status);
        Assert.Equal(Now, loan.ReturnedAtUtc);
        Assert.Equal("Checked cover.", loan.ReturnNote);
        Assert.Equal(BookCopyStatus.Available, await CopyStatusAsync());

        Assert.Empty(await Db(db =>
            db.PenaltyRecords
                .Where(x => x.LoanId == loanId)
                .ToListAsync()));

        LoanReturned returned = Assert.Single(
            (await EventsAsync<LoanReturned>()),
            x => x.LoanId == loanId);

        Assert.Equal(BookId, returned.BookId);
        Assert.Equal(CopyId, returned.BookCopyId);
        Assert.Equal(StaffId, returned.StaffMemberId);
        Assert.Equal(Now, returned.ReturnedAtUtc);
        Assert.Equal(0, returned.OverdueWorkingDays);

        Assert.DoesNotContain(
            (await EventsAsync<PenaltyApplied>()),
            x => x.LoanId == loanId);
    }

    [Fact]
    public async Task Undamaged_return_assigns_first_reservation_and_renumbers_queue()
    {
        Guid loanId = await SeedAsync(queue: true);

        using var client = Client();
        using var response = await ReturnAsync(client, loanId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            LoanStatus.Returned,
            (await ReadLoanAsync(loanId)).Status);

        List<Reservation> reservations = await Db(db =>
            db.Reservations
                .AsNoTracking()
                .Where(x => x.BookId == BookId)
                .OrderBy(x => x.CreatedAtUtc)
                .ToListAsync());

        Assert.Equal(2, reservations.Count);

        Reservation first = reservations[0];
        Reservation second = reservations[1];

        Assert.Equal(ReservationStatus.ReadyForPickup, first.Status);
        Assert.Equal(CopyId, first.CopyId);
        Assert.Equal(0, first.QueuePosition);

        // Tuesday + three working days, excluding Tuesday:
        // Wednesday, Thursday, Friday.
        // Friday end in Yerevan = immediately before Friday 20:00 UTC.
        DateTime expectedDeadline =
            new DateTime(
                2026, 10, 23, 20, 0, 0, DateTimeKind.Utc)
            .AddTicks(-1);

        Assert.Equal(expectedDeadline, first.ReadyUntilUtc);

        Assert.Equal(ReservationStatus.Waiting, second.Status);
        Assert.Equal(1, second.QueuePosition);
        Assert.Null(second.CopyId);
        Assert.Null(second.ReadyUntilUtc);

        Assert.Equal(BookCopyStatus.Reserved, await CopyStatusAsync());

        ReservationReady ready = Assert.Single(
            (await EventsAsync<ReservationReady>()),
            x => x.BookId == BookId);

        Assert.Equal(first.Id, ready.ReservationId);
        Assert.Equal(first.StaffMemberId, ready.StaffMemberId);
        Assert.Equal(first.ReadyUntilUtc, ready.ReadyUntilUtc);
    }

    [Theory]
    [InlineData(0, StaffMemberStatus.Restricted)]
    [InlineData(5, StaffMemberStatus.Suspended)]
    public async Task Damage_adds_points_and_changes_staff_status(
        int priorPoints,
        StaffMemberStatus expectedStatus)
    {
        Guid loanId = await SeedAsync(
            priorPoints: priorPoints,
            queue: true);

        using var client = Client();
        using var response = await ReturnAsync(
            client,
            loanId,
            damaged: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedStatus, await StaffStatusAsync());
        Assert.Equal(BookCopyStatus.UnderRepair, await CopyStatusAsync());

        List<PenaltyRecord> newPenalties = await Db(db =>
            db.PenaltyRecords
                .AsNoTracking()
                .Where(x =>
                    x.LoanId == loanId &&
                    x.CreatedAtUtc == Now)
                .ToListAsync());

        PenaltyRecord penalty = Assert.Single(newPenalties);

        Assert.Equal(5, penalty.Points);
        Assert.Equal(StaffId, penalty.StaffMemberId);
        Assert.Equal(PenaltyRecordStatus.Active, penalty.Status);
        Assert.Equal(Now.AddDays(90), penalty.ExpiresAtUtc);

        PenaltyApplied applied = Assert.Single(
            (await EventsAsync<PenaltyApplied>()),
            x => x.LoanId == loanId);

        Assert.Equal(penalty.Id, applied.PenaltyRecordId);
        Assert.Equal(5, applied.Points);
        Assert.Equal(priorPoints + 5, applied.ActivePointsTotal);

        StaffStatusChanged changed = Assert.Single(
            (await EventsAsync<StaffStatusChanged>()),
            x => x.StaffMemberId == StaffId);

        Assert.Equal(expectedStatus, changed.NewStatus);

        Assert.Equal(
            priorPoints == 0
                ? StaffMemberStatus.Active
                : StaffMemberStatus.Restricted,
            changed.OldStatus);

        if (expectedStatus == StaffMemberStatus.Suspended)
        {
            Assert.Equal(
                Now.AddDays(30),
                changed.EffectiveUntilUtc);
        }
        else
        {
            Assert.Null(changed.EffectiveUntilUtc);
        }

        // A damaged copy must not be offered to the queue.
        List<Reservation> reservations = await ReadQueueAsync();

        Assert.Equal(
            new[] { 1, 2 },
            reservations.Select(x => x.QueuePosition).ToArray());

        Assert.All(reservations, reservation =>
        {
            Assert.Equal(
                ReservationStatus.Waiting,
                reservation.Status);

            Assert.Null(reservation.CopyId);
            Assert.Null(reservation.ReadyUntilUtc);
        });

        Assert.DoesNotContain(
            (await EventsAsync<ReservationReady>()),
            x => x.BookId == BookId);
    }

    /// <summary>
    /// A loan already marked Overdue is returned on its due-date local day: with NextLocalDay the dates say "not late",
    /// but the stored status makes it late — one 0-point record, no PenaltyApplied, no status change (BQ-BA-03).
    /// </summary>
    [Fact]
    public async Task Returning_an_overdue_loan_on_its_due_date_records_zero_points()
    {
        Guid loanId = await SeedAsync(overdueToday: true);

        using var client = Client();
        using var response = await ReturnAsync(client, loanId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result =
            (await response.Content.ReadFromJsonAsync<ReturnLoanResponse>(
                Json))!;
        Assert.Equal(0, result.OverdueWorkingDays);
        Assert.Equal(0, result.PenaltyPointsApplied);

        int[] points = await Db(db =>
            db.PenaltyRecords
                .Where(x => x.LoanId == loanId)
                .Select(x => x.Points)
                .ToArrayAsync());
        Assert.Equal(new[] { 0 }, points);

        Assert.DoesNotContain(
            (await EventsAsync<PenaltyApplied>()),
            x => x.LoanId == loanId);
        Assert.Equal(StaffMemberStatus.Active, await StaffStatusAsync());
    }

    [Fact]
    public async Task Late_and_damaged_return_stores_two_penalties_and_one_status_change()
    {
        Guid loanId = await SeedAsync(late: true);

        using var client = Client();
        using var response = await ReturnAsync(
            client,
            loanId,
            damaged: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result =
            (await response.Content.ReadFromJsonAsync<ReturnLoanResponse>(
                Json))!;

        // Due Friday, returned Tuesday: Monday and Tuesday count.
        Assert.Equal(2, result.OverdueWorkingDays);
        Assert.Equal(7, result.PenaltyPointsApplied);

        int[] points = await Db(db =>
            db.PenaltyRecords
                .Where(x => x.LoanId == loanId)
                .OrderBy(x => x.Points)
                .Select(x => x.Points)
                .ToArrayAsync());

        Assert.Equal(new[] { 2, 5 }, points);

        // Points expire after the configured lifetime (policy table: 90 days).
        DateTime[] expiries = await Db(db =>
            db.PenaltyRecords
                .Where(x => x.LoanId == loanId)
                .Select(x => x.ExpiresAtUtc)
                .ToArrayAsync());
        Assert.All(expiries, expiry => Assert.Equal(Now.AddDays(90), expiry));
        Assert.Equal(
            StaffMemberStatus.Restricted,
            await StaffStatusAsync());

        Assert.Equal(
            2,
            (await EventsAsync<PenaltyApplied>())
            .Count(x => x.LoanId == loanId));

        Assert.Single(
            (await EventsAsync<StaffStatusChanged>()),
            x => x.StaffMemberId == StaffId);
    }

    [Fact]
    public async Task Failure_after_database_save_rolls_back_every_return_change()
    {
        // Existing 3 points + 2 late points would change Active to Restricted.
        // The returned copy would also become reserved.
        Guid loanId = await SeedAsync(
            late: true,
            priorPoints: 3,
            queue: true);

        int outboxBefore = await Db(db =>
            db.OutboxMessages.CountAsync());

        _host.FailAfterSave = true;

        using var client = Client();
        using var response = await ReturnAsync(
            client,
            loanId,
            note: "Must roll back");

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        // Proves the failure occurred after SQL writes, not during setup.
        Assert.True(_host.SavedBeforeFailure);

        // Every read uses a fresh scope/context.
        Loan loan = await ReadLoanAsync(loanId);

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Null(loan.ReturnedAtUtc);
        Assert.Null(loan.ReturnNote);

        Assert.Equal(BookCopyStatus.OnLoan, await CopyStatusAsync());

        Assert.Equal(
            StaffMemberStatus.Active,
            await StaffStatusAsync());

        int[] points = await Db(db =>
            db.PenaltyRecords
                .Where(x => x.LoanId == loanId)
                .Select(x => x.Points)
                .ToArrayAsync());

        // Only the penalty that existed before the return remains.
        Assert.Equal(new[] { 3 }, points);

        List<Reservation> reservations = await ReadQueueAsync();

        Assert.Equal(
            new[] { 1, 2 },
            reservations.Select(x => x.QueuePosition).ToArray());

        Assert.All(reservations, reservation =>
        {
            Assert.Equal(
                ReservationStatus.Waiting,
                reservation.Status);

            Assert.Null(reservation.CopyId);
            Assert.Null(reservation.ReadyUntilUtc);
        });

        Assert.Equal(
            outboxBefore,
            await Db(db => db.OutboxMessages.CountAsync()));
    }

    /// <summary>
    /// Policy table: a damaged copy goes UnderRepair. The queue keeps waiting — nobody gets a copy that cannot be lent.
    /// </summary>
    [Fact]
    public async Task Damaged_return_keeps_the_queue_waiting()
    {
        Guid loanId = await SeedAsync(queue: true);

        using var client = Client();
        using var response = await ReturnAsync(client, loanId, damaged: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookCopyStatus.UnderRepair, await CopyStatusAsync());

        int[] positions = await Db(db =>
            db.Reservations
                .Where(x => x.BookId == BookId && x.Status == ReservationStatus.Waiting)
                .OrderBy(x => x.QueuePosition)
                .Select(x => x.QueuePosition)
                .ToArrayAsync());
        Assert.Equal(new[] { 1, 2 }, positions);
        Assert.DoesNotContain(
            (await EventsAsync<ReservationReady>()),
            x => x.BookId == BookId);
    }

    /// <summary>
    /// Two librarians return the same loan at the same moment (BR-BOR-4, NFR-08): the staff lock serialises them, one
    /// return succeeds, the other is refused, and the penalties and events exist once.
    /// </summary>
    [Fact]
    public async Task Two_simultaneous_returns_close_the_loan_once()
    {
        Guid loanId = await SeedAsync(late: true);

        using var first = Client();
        using var second = Client();
        HttpResponseMessage[] responses = await Task.WhenAll(
            ReturnAsync(first, loanId),
            ReturnAsync(second, loanId));

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity },
            responses.Select(r => r.StatusCode).OrderBy(c => (int)c));
        foreach (var response in responses)
            response.Dispose();

        Assert.Equal(1, await Db(db => db.PenaltyRecords.CountAsync(x => x.LoanId == loanId)));
        Assert.Single(await EventsAsync<LoanReturned>(), x => x.LoanId == loanId);
    }

    [Fact]
    public async Task Duplicate_return_is_422_without_duplicate_records_or_events()
    {
        Guid loanId = await SeedAsync();

        using var client = Client();

        using var first = await ReturnAsync(
            client,
            loanId,
            damaged: true);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        int eventsBefore = await Db(db =>
            db.OutboxMessages.CountAsync());

        using var second = await ReturnAsync(
            client,
            loanId,
            damaged: true);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            second.StatusCode);

        JsonElement body =
            await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            BorrowingErrors.LoanAlreadyClosed.Code,
            body.GetProperty("code").GetString());

        Assert.Equal(
            1,
            await Db(db =>
                db.PenaltyRecords.CountAsync(
                    x => x.LoanId == loanId)));

        Assert.Equal(
            eventsBefore,
            await Db(db => db.OutboxMessages.CountAsync()));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    public async Task Return_requires_librarian_or_administrator(
        string? role,
        HttpStatusCode expectedStatus)
    {
        Guid loanId = await SeedAsync();

        using var client = Client(role);
        using var response = await ReturnAsync(client, loanId);

        Assert.Equal(expectedStatus, response.StatusCode);

        Assert.Equal(
            LoanStatus.Active,
            (await ReadLoanAsync(loanId)).Status);

        Assert.Equal(BookCopyStatus.OnLoan, await CopyStatusAsync());
    }

    [Fact]
    public async Task Long_note_is_400_without_changing_the_loan()
    {
        Guid loanId = await SeedAsync();

        using var client = Client();
        using var response = await ReturnAsync(
            client,
            loanId,
            note: new string('a', Loan.ReturnNoteMaxLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement body =
            await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(
            body.GetProperty("errors").TryGetProperty("Note", out _));

        Loan loan = await ReadLoanAsync(loanId);

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Null(loan.ReturnedAtUtc);
        Assert.Null(loan.ReturnNote);

        Assert.Equal(BookCopyStatus.OnLoan, await CopyStatusAsync());
    }

    [Fact]
    public async Task Unknown_loan_is_404()
    {
        using var client = Client();
        using var response = await ReturnAsync(
            client,
            Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body =
            await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            BorrowingErrors.LoanNotFound.Code,
            body.GetProperty("code").GetString());
    }

    private HttpClient Client(string? role = StaffRoles.Librarian)
    {
        HttpClient client = _host.CreateClient();

        if (role is not null)
        {
            var account = new LoginAccount(
                Guid.NewGuid(),
                "return-test@ameriabank.am",
                Guid.NewGuid(),
                StaffMemberStatus.Active,
                [role]);

            // The business clock of the host is fixed (Now); the token must be valid at the real time the test
            // runs, otherwise the API answers 401 on every day except the fixed one.
            var issuer = new JwtTokenIssuer(
                _host.Services.GetRequiredService<IOptions<JwtOptions>>(),
                new ReturnClock(() => DateTime.UtcNow));
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
    /// The return operation itself is always performed through HTTP.
    /// </summary>
    private Task<Guid> SeedAsync(
        bool late = false,
        int priorPoints = 0,
        bool queue = false,
        bool overdueToday = false) =>
        Db(async db =>
        {
            var staff = await db.StaffMembers
                .SingleAsync(x => x.Id == StaffId);

            staff.ChangeStatus(
                priorPoints >= 5
                    ? StaffMemberStatus.Restricted
                    : StaffMemberStatus.Active);

            var copy = await db.BookCopies
                .SingleAsync(x => x.Id == CopyId);

            copy.ChangeStatus(BookCopyStatus.OnLoan);

            // overdueToday: due two hours before Now on the same local day (Tuesday 14:00 Yerevan).
            DateTime dueAtUtc = late
                ? new DateTime(
                    2026, 10, 16, 12, 0, 0, DateTimeKind.Utc)
                : overdueToday ? Now.AddHours(-2) : Now.AddDays(1);

            Loan loan = Loan.Issue(
                Guid.NewGuid(),
                CopyId,
                StaffId,
                dueAtUtc.AddDays(-14),
                14);

            if (overdueToday)
                loan.MarkOverdue(Now);   // what the daily job may do once the due time has passed

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

    private static Task<HttpResponseMessage> ReturnAsync(
        HttpClient client,
        Guid loanId,
        bool damaged = false,
        string? note = null) =>
        client.PostAsJsonAsync(
            $"/api/loans/{loanId}/return",
            new ReturnLoanRequest(damaged, note));

    private Task<Loan> ReadLoanAsync(Guid loanId) =>
        Db(db => db.Loans
            .AsNoTracking()
            .SingleAsync(x => x.Id == loanId));

    private Task<BookCopyStatus> CopyStatusAsync() =>
        Db(db => db.BookCopies
            .Where(x => x.Id == CopyId)
            .Select(x => x.Status)
            .SingleAsync());

    private Task<StaffMemberStatus> StaffStatusAsync() =>
        Db(db => db.StaffMembers
            .Where(x => x.Id == StaffId)
            .Select(x => x.Status)
            .SingleAsync());

    private Task<List<Reservation>> ReadQueueAsync() =>
        Db(db => db.Reservations
            .AsNoTracking()
            .Where(x => x.BookId == BookId)
            .OrderBy(x => x.QueuePosition)
            .ToListAsync());

    private Task<List<T>> EventsAsync<T>()
        where T : class, IDomainEvent =>
        Db(async db =>
        {
            List<OutboxMessage> messages = await db.OutboxMessages
                .AsNoTracking()
                .Where(x => x.Type == typeof(T).Name)
                .ToListAsync();

            return messages
                .Select(x =>
                    JsonSerializer.Deserialize<T>(x.Payload, Json)!)
                .ToList();
        });

    /// <summary>
    /// Uses the existing real API fixture, with a fixed clock,
    /// no Borrowing seed, and an optional failure-injection event bus.
    /// </summary>
    private sealed class ReturnHost : ApiHostFactory
    {
        public bool FailAfterSave { get; set; }

        public bool SavedBeforeFailure { get; set; }

        protected override void ConfigureTestServices(
            IServiceCollection services)
        {
            ServiceDescriptor borrowingSeed = services.Single(
                descriptor =>
                    descriptor.ServiceType == typeof(IModuleSeeder) &&
                    descriptor.ImplementationType ==
                    typeof(BorrowingSeeder));

            services.Remove(borrowingSeed);

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new ReturnClock(() => Now));

            services.RemoveAll<IEventBus>();

            services.AddScoped<IEventBus>(provider =>
                new TestOutboxBus(
                    provider.GetRequiredService<LibraryDbContext>(),
                    provider.GetRequiredService<EventTypeRegistry>(),
                    this));
        }
    }

    /// <summary>
    /// Uses the real outbox implementation.
    /// Only the rollback test enables the deliberate failure.
    /// </summary>
    private sealed class TestOutboxBus(
        LibraryDbContext db,
        EventTypeRegistry registry,
        ReturnHost host) : IEventBus
    {
        private readonly OutboxEventBus _inner = new(db, registry);

        public async Task PublishAsync<TEvent>(
            TEvent domainEvent,
            CancellationToken ct = default)
            where TEvent : class, IDomainEvent
        {
            await _inner.PublishAsync(domainEvent, ct);

            if (host.FailAfterSave && domainEvent is LoanReturned)
            {
                // Test only: flush all staged changes to SQL Server.
                // The caller's transaction remains open and uncommitted.
                await db.SaveChangesAsync(ct);

                host.SavedBeforeFailure = true;

                throw new InvalidOperationException(
                    "Deliberate return integration-test failure.");
            }
        }
    }

    private sealed class ReturnClock(Func<DateTime> now) : IClock
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
            StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);
    }
}