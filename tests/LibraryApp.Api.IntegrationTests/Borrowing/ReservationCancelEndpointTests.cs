using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// Cancelling a reservation (BOR-12, NFR-03) through HTTP on the real host and database: who may cancel, which states
/// may be cancelled, the hand-over of a held copy with its event, one transaction, and a cancel racing the issue of the
/// held copy. Every test has its own host and database, so the books and staff members are not shared.
/// </summary>
public sealed class ReservationCancelEndpointTests : IAsyncLifetime
{
    private const int Book = 57;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly Guid BookId = CatalogSeedIds.Book(Book);

    private static readonly Guid CopyId = CatalogSeedIds.Copy(Book, 1);

    private readonly CancelHost _host = new();

    public Task InitializeAsync() => _host.InitializeAsync();

    public Task DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Anonymous_unknown_and_another_members_reservation_are_refused_without_changes()
    {
        (Guid ready, _) = await HoldCopyAsync(Members.AnnaPetrosyan);

        using (var anonymous = Client(null))
        using (var response = await CancelAsync(anonymous, ready))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using (var librarian = Client(StaffRoles.Librarian))
            await AssertProblemAsync(await CancelAsync(librarian, Guid.NewGuid()),
                HttpStatusCode.NotFound, BorrowingErrors.ReservationNotFound.Code);

        // NFR-03: another member's reservation is reported as not found, not as forbidden.
        using (var other = Client(StaffRoles.StaffMember, Members.DavitHakobyan))
            await AssertProblemAsync(await CancelAsync(other, ready),
                HttpStatusCode.NotFound, BorrowingErrors.ReservationNotFound.Code);

        Assert.Equal(ReservationStatus.ReadyForPickup, (await ReadAsync(ready)).Status);
        Assert.Equal(BookCopyStatus.Reserved, await CopyStatusAsync());
    }

    [Theory]
    [InlineData(StaffRoles.StaffMember)]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task A_held_copy_nobody_waits_for_becomes_available(string role)
    {
        (Guid ready, _) = await HoldCopyAsync(Members.AnnaPetrosyan);
        int eventsBefore = await Db(db => db.OutboxMessages.CountAsync());

        using var client = Client(role, Members.AnnaPetrosyan);
        using var response = await CancelAsync(client, ready);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ReservationDto>(Json))!;
        Assert.Equal((ReservationStatus.Cancelled, 0), (result.Status, result.QueuePosition));
        Assert.Equal(ReservationStatus.Cancelled, (await ReadAsync(ready)).Status);
        Assert.Equal(BookCopyStatus.Available, await CopyStatusAsync());
        Assert.Equal(eventsBefore, await Db(db => db.OutboxMessages.CountAsync()));
    }

    /// <summary>
    /// The held copy goes to the first waiting member in the same transaction. Both orders of the two reservation ids:
    /// EF writes updates in key order and the database allows one ReadyForPickup reservation per copy, so the cancelled
    /// one must be written first whatever the ids are.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_held_copy_goes_to_the_next_member_with_ReservationReady(bool holderIdFirst)
    {
        Guid low = Guid.Parse("00000000-0000-0000-0000-000000000057");
        Guid high = Guid.Parse("ffffffff-ffff-ffff-ffff-000000000057");
        (Guid ready, Guid[] waiting) = await HoldCopyAsync(
            Members.AnnaPetrosyan,
            holderIdFirst ? low : high,
            [holderIdFirst ? high : low, Guid.NewGuid()],
            Members.MariamAvetisyan, Members.DavitHakobyan);

        using var holder = Client(StaffRoles.StaffMember, Members.AnnaPetrosyan);
        using (var response = await CancelAsync(holder, ready))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Reservation next = await ReadAsync(waiting[0]);
        Assert.Equal(ReservationStatus.ReadyForPickup, next.Status);
        Assert.Equal(CopyId, next.CopyId);
        Assert.Equal(ExpectedReadyUntilUtc(), next.ReadyUntilUtc);
        Reservation last = await ReadAsync(waiting[1]);
        Assert.Equal((ReservationStatus.Waiting, 1), (last.Status, last.QueuePosition));
        Assert.Equal(BookCopyStatus.Reserved, await CopyStatusAsync());

        ReservationReady published = Assert.Single(await EventsAsync<ReservationReady>());
        Assert.Equal(
            (waiting[0], BookId, Members.MariamAvetisyan, next.ReadyUntilUtc!.Value),
            (published.ReservationId, published.BookId, published.StaffMemberId, published.ReadyUntilUtc));

        // The former holder can no longer collect the copy: it is held for the next member now.
        using var librarian = Client(StaffRoles.Librarian);
        await AssertProblemAsync(
            await librarian.PostAsJsonAsync("/api/loans",
                new IssueLoanRequest(Members.AnnaPetrosyan, CatalogSeedIds.CopyBarcode(Book, 1))),
            HttpStatusCode.UnprocessableEntity, BorrowingErrors.CopyNotAvailable.Code);
    }

    [Theory]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Fulfilled)]
    [InlineData(ReservationStatus.Expired)]
    public async Task A_closed_reservation_cannot_be_cancelled(ReservationStatus status)
    {
        DateTime now = DateTime.UtcNow;
        var reservation = Reservation.Create(Guid.NewGuid(), BookId, Members.AnnaPetrosyan, now.AddDays(-5), 1);
        if (status == ReservationStatus.Cancelled)
            reservation.Cancel();
        else
        {
            reservation.MarkReady(CopyId, now.AddDays(-1));
            if (status == ReservationStatus.Fulfilled)
                reservation.Fulfil();
            else
                reservation.Expire(now);
        }
        await Db(db =>
        {
            db.Reservations.Add(reservation);
            return db.SaveChangesAsync();
        });

        using var client = Client(StaffRoles.StaffMember, Members.AnnaPetrosyan);
        await AssertProblemAsync(await CancelAsync(client, reservation.Id),
            HttpStatusCode.UnprocessableEntity, BorrowingErrors.ReservationCannotBeCancelled.Code);

        Assert.Equal(status, (await ReadAsync(reservation.Id)).Status);
        Assert.Equal(BookCopyStatus.Available, await CopyStatusAsync());
    }

    [Fact]
    public async Task A_failure_after_the_sql_writes_rolls_back_the_whole_cancellation()
    {
        (Guid ready, Guid[] waiting) = await HoldCopyAsync(Members.AnnaPetrosyan, null, null, Members.MariamAvetisyan);
        int eventsBefore = await Db(db => db.OutboxMessages.CountAsync());
        _host.FailOnReservationReady = true;

        using var client = Client(StaffRoles.Librarian);
        using (var response = await CancelAsync(client, ready))
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Assert.True(_host.SavedBeforeFailure);
        Assert.Equal(ReservationStatus.ReadyForPickup, (await ReadAsync(ready)).Status);
        Reservation next = await ReadAsync(waiting[0]);
        Assert.Equal((ReservationStatus.Waiting, 1, (Guid?)null), (next.Status, next.QueuePosition, next.CopyId));
        Assert.Equal(BookCopyStatus.Reserved, await CopyStatusAsync());
        Assert.Equal(eventsBefore, await Db(db => db.OutboxMessages.CountAsync()));
    }

    /// <summary>
    /// The holder collects the copy while someone cancels the reservation: the book lock lets one of them through,
    /// the other sees the result and is refused with 422, and the copy ends in one consistent state.
    /// </summary>
    [Fact]
    public async Task Cancel_racing_the_issue_of_the_held_copy_leaves_one_consistent_outcome()
    {
        (Guid ready, Guid[] waiting) = await HoldCopyAsync(Members.AnnaPetrosyan, null, null, Members.MariamAvetisyan);

        using var holder = Client(StaffRoles.StaffMember, Members.AnnaPetrosyan);
        using var librarian = Client(StaffRoles.Librarian);
        HttpResponseMessage[] responses = await Task.WhenAll(
            CancelAsync(holder, ready),
            librarian.PostAsJsonAsync("/api/loans",
                new IssueLoanRequest(Members.AnnaPetrosyan, CatalogSeedIds.CopyBarcode(Book, 1))));
        try
        {
            (HttpStatusCode cancel, HttpStatusCode issue) = (responses[0].StatusCode, responses[1].StatusCode);
            Reservation holderReservation = await ReadAsync(ready);
            Reservation next = await ReadAsync(waiting[0]);
            int loans = await Db(db => db.Loans.CountAsync(loan => loan.BookCopyId == CopyId));

            if (cancel == HttpStatusCode.OK)
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, issue);
                Assert.Equal(ReservationStatus.Cancelled, holderReservation.Status);
                Assert.Equal((ReservationStatus.ReadyForPickup, (Guid?)CopyId), (next.Status, next.CopyId));
                Assert.Equal(BookCopyStatus.Reserved, await CopyStatusAsync());
                Assert.Equal(0, loans);
            }
            else
            {
                Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.Created), (cancel, issue));
                Assert.Equal(BorrowingErrors.ReservationCannotBeCancelled.Code, await CodeAsync(responses[0]));
                Assert.Equal(ReservationStatus.Fulfilled, holderReservation.Status);
                Assert.Equal((ReservationStatus.Waiting, 1), (next.Status, next.QueuePosition));
                Assert.Equal(BookCopyStatus.OnLoan, await CopyStatusAsync());
                Assert.Equal(1, loans);
            }
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Two_parallel_cancels_of_a_held_copy_hand_it_over_once()
    {
        (Guid ready, Guid[] waiting) = await HoldCopyAsync(
            Members.AnnaPetrosyan, null, null, Members.MariamAvetisyan, Members.DavitHakobyan);

        using var first = Client(StaffRoles.Librarian);
        using var second = Client(StaffRoles.StaffMember, Members.AnnaPetrosyan);
        HttpResponseMessage[] responses = await Task.WhenAll(CancelAsync(first, ready), CancelAsync(second, ready));
        try
        {
            Assert.Equal(
                new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity },
                responses.Select(response => response.StatusCode).OrderBy(status => (int)status));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
                response.Dispose();
        }

        Assert.Equal(ReservationStatus.ReadyForPickup, (await ReadAsync(waiting[0])).Status);
        Reservation last = await ReadAsync(waiting[1]);
        Assert.Equal((ReservationStatus.Waiting, 1), (last.Status, last.QueuePosition));
        Assert.Single(await EventsAsync<ReservationReady>());
    }

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, Guid reservationId) =>
        client.PostAsync($"/api/reservations/{reservationId}/cancel", null);

    private HttpClient Client(string? role, Guid? staffMemberId = null)
    {
        HttpClient client = _host.CreateClient();
        if (role is not null)
        {
            var account = _host.Account(role);
            if (staffMemberId is { } id && role == StaffRoles.StaffMember)
                account = account with { StaffMemberId = id };
            string token = _host.Services.GetRequiredService<IAccessTokenIssuer>().Issue(account).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await CodeAsync(response));
        }
    }

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> action)
    {
        using var scope = _host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    /// <summary>
    /// Copy 1 of the book is held for <paramref name="holder"/> (ReadyForPickup); <paramref name="waiting"/> wait in this
    /// order. Ids may be fixed to control the order in which EF writes the rows.
    /// </summary>
    private async Task<(Guid Ready, Guid[] Waiting)> HoldCopyAsync(
        Guid holder, Guid? readyId = null, Guid[]? waitingIds = null, params Guid[] waiting)
    {
        DateTime now = DateTime.UtcNow;
        var ready = Reservation.Create(readyId ?? Guid.NewGuid(), BookId, holder, now.AddHours(-3), 1);
        ready.MarkReady(CopyId, now.AddDays(3));
        Guid[] ids = waiting.Select((_, index) => waitingIds?[index] ?? Guid.NewGuid()).ToArray();

        await Db(async db =>
        {
            (await db.BookCopies.SingleAsync(copy => copy.Id == CopyId)).ChangeStatus(BookCopyStatus.Reserved);
            db.Reservations.Add(ready);
            for (int index = 0; index < waiting.Length; index++)
                db.Reservations.Add(Reservation.Create(ids[index], BookId, waiting[index], now.AddHours(-2 + index * 0.1), index + 1));
            return await db.SaveChangesAsync();
        });

        return (ready.Id, ids);
    }

    private Task<Reservation> ReadAsync(Guid reservationId) =>
        Db(db => db.Reservations.AsNoTracking().SingleAsync(reservation => reservation.Id == reservationId));

    private Task<BookCopyStatus> CopyStatusAsync() =>
        Db(db => db.BookCopies.Where(copy => copy.Id == CopyId).Select(copy => copy.Status).SingleAsync());

    /// <summary>The pickup deadline the settings give today (BOR-07, <c>Borrowing:Reservations</c>).</summary>
    private DateTime ExpectedReadyUntilUtc() => ReservationQueueService.ReadyUntilUtc(
        _host.Services.GetRequiredService<IOptions<BorrowingOptions>>().Value.Reservations,
        _host.Services.GetRequiredService<IWorkingDayCalendar>(),
        _host.Services.GetRequiredService<IClock>());

    private Task<List<T>> EventsAsync<T>() where T : class, IDomainEvent =>
        Db(async db => (await db.OutboxMessages.AsNoTracking()
                .Where(message => message.Type == typeof(T).Name)
                .ToListAsync())
            .Select(message => JsonSerializer.Deserialize<T>(message.Payload, Json)!)
            .ToList());

    /// <summary>The API without the Borrowing seed, with an outbox that can fail after the SQL writes on demand.</summary>
    private sealed class CancelHost : ApiHostFactory
    {
        public bool FailOnReservationReady { get; set; }

        public bool SavedBeforeFailure { get; set; }

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            services.Remove(services.Single(descriptor =>
                descriptor.ServiceType == typeof(IModuleSeeder) && descriptor.ImplementationType == typeof(BorrowingSeeder)));

            services.RemoveAll<IEventBus>();
            services.AddScoped<IEventBus>(provider => new FailingOutboxBus(
                provider.GetRequiredService<LibraryDbContext>(),
                provider.GetRequiredService<EventTypeRegistry>(),
                this));
        }
    }

    /// <summary>The real outbox; when asked, writes the staged changes and then fails, so the rollback is visible.</summary>
    private sealed class FailingOutboxBus(LibraryDbContext db, EventTypeRegistry registry, CancelHost host) : IEventBus
    {
        private readonly OutboxEventBus _inner = new(db, registry);

        public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
            where TEvent : class, IDomainEvent
        {
            await _inner.PublishAsync(domainEvent, ct);

            if (host.FailOnReservationReady && domainEvent is ReservationReady)
            {
                await db.SaveChangesAsync(ct);
                host.SavedBeforeFailure = true;
                throw new InvalidOperationException("Deliberate reservation cancel integration-test failure.");
            }
        }
    }
}
