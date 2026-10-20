using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// Reservations (BOR-07, BR-BOR-2, BR-BOR-3, NFR-03) on the real host and database with the Catalog and Staff seed.
/// Every test uses its own books (one copy each where the book must be fully lent) and its own staff members.
/// </summary>
public class ReservationEndpointTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private HttpClient Client(string? role, Guid? staffMemberId = null)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                factory.Account(role) with { StaffMemberId = staffMemberId ?? Guid.NewGuid() }).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private HttpClient Librarian() => Client(StaffRoles.Librarian);

    private HttpClient Reader(Guid staffMemberId) => Client(StaffRoles.StaffMember, staffMemberId);

    private static Task<HttpResponseMessage> Reserve(HttpClient client, int book, Guid? staffMemberId = null) =>
        client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest(CatalogSeedIds.Book(book), staffMemberId));

    private static async Task<ReservationDto> Read(HttpClient client, Guid reservationId) =>
        (await client.GetFromJsonAsync<ReservationDto>($"/api/reservations/{reservationId}", Json))!;

    private static async Task<ReservationDto> Created(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReservationDto>(Json))!;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> operation)
    {
        using var scope = factory.Services.CreateScope();
        return await operation(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    /// <summary>All copies of the book go on loan, so it can be reserved (BR-BOR-3).</summary>
    private async Task LendAllCopiesAsync(params int[] books)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var bookIds = books.Select(CatalogSeedIds.Book).ToArray();
        foreach (var copy in await db.BookCopies.Where(c => bookIds.Contains(c.BookId)).ToListAsync())
            copy.ChangeStatus(BookCopyStatus.OnLoan);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_staff_member_reserves_for_themselves_and_only_they_can_read_it()
    {
        await LendAllCopiesAsync(44);
        using var anna = Reader(Members.AnnaPetrosyan);

        var response = await Reserve(anna, 44);
        var reservation = await Created(response);

        Assert.Equal(Members.AnnaPetrosyan, reservation.StaffMemberId);
        Assert.Equal(ReservationStatus.Waiting, reservation.Status);
        Assert.Equal(1, reservation.QueuePosition);
        Assert.Equal(reservation, await anna.GetFromJsonAsync<ReservationDto>(response.Headers.Location, Json));
        Assert.Equal(reservation, await Librarian().GetFromJsonAsync<ReservationDto>(response.Headers.Location, Json));
        await AssertProblem(await Reader(Members.DavitHakobyan).GetAsync(response.Headers.Location),
            HttpStatusCode.NotFound, "borrowing.reservation_not_found");
    }

    [Fact]
    public async Task Who_may_reserve_for_whom()
    {
        await LendAllCopiesAsync(43);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Reserve(Client(null), 43, Members.AnnaPetrosyan)).StatusCode);
        await AssertProblem(await Reserve(Reader(Members.AnnaPetrosyan), 43, Members.DavitHakobyan),
            HttpStatusCode.Forbidden, "borrowing.reservation_for_another_staff_member");
        await AssertProblem(await Reserve(Librarian(), 43),
            HttpStatusCode.BadRequest, "borrowing.reservation_staff_member_required");

        var invalid = await Librarian().PostAsJsonAsync("/api/reservations", new { bookId = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")
            .TryGetProperty(nameof(CreateReservationRequest.BookId), out _));
    }

    [Fact]
    public async Task A_book_with_an_available_copy_cannot_be_reserved_and_an_unknown_one_is_404()
    {
        await AssertProblem(await Reserve(Librarian(), 50, Members.MariamAvetisyan),
            HttpStatusCode.UnprocessableEntity, "borrowing.reservation_copy_available");
        var unknown = await Librarian().PostAsJsonAsync("/api/reservations",
            new CreateReservationRequest(Guid.NewGuid(), Members.MariamAvetisyan));
        await AssertProblem(unknown, HttpStatusCode.NotFound, "catalog.book_not_found");
    }

    [Fact]
    public async Task Restricted_and_suspended_staff_members_cannot_reserve()
    {
        await LendAllCopiesAsync(46);

        await AssertProblem(await Reserve(Librarian(), 46, Members.GorMkrtchyan),
            HttpStatusCode.UnprocessableEntity, "borrowing.restricted_reservation_forbidden");
        await AssertProblem(await Reserve(Librarian(), 46, Members.SonaVardanyan),
            HttpStatusCode.UnprocessableEntity, StaffErrors.Suspended.Code);
    }

    [Fact]
    public async Task A_held_book_cannot_be_reserved_and_the_same_queue_twice_is_409()
    {
        // Lilit borrows the only copy of book 47 through the real issue endpoint.
        var issued = await Librarian().PostAsJsonAsync("/api/loans",
            new IssueLoanRequest(Members.LilitGrigoryan, CatalogSeedIds.CopyBarcode(47, 1)));
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);

        await AssertProblem(await Reserve(Librarian(), 47, Members.LilitGrigoryan),
            HttpStatusCode.UnprocessableEntity, "borrowing.book_already_on_loan");

        await Created(await Reserve(Librarian(), 47, Members.DavitHakobyan));
        await AssertProblem(await Reserve(Librarian(), 47, Members.DavitHakobyan),
            HttpStatusCode.Conflict, "borrowing.duplicate_active_reservation");
    }

    [Fact]
    public async Task The_fourth_active_reservation_is_refused()
    {
        await LendAllCopiesAsync(48, 49, 55, 56);
        using var client = Librarian();

        foreach (int book in new[] { 48, 49, 55 })
            await Created(await Reserve(client, book, Members.AramSargsyan));

        await AssertProblem(await Reserve(client, 56, Members.AramSargsyan),
            HttpStatusCode.UnprocessableEntity, "borrowing.reservation_limit_reached");
    }

    /// <summary>
    /// Three people reserve the same book at the same moment. The queue lock of the book (BorrowingLocks.Book) gives the
    /// positions one request at a time: 1, 2 and 3, never two equal positions.
    /// </summary>
    [Fact]
    public async Task Parallel_reservations_of_one_book_get_distinct_queue_positions()
    {
        await LendAllCopiesAsync(45);
        Guid[] members = [Members.MariamAvetisyan, Members.DavitHakobyan, Members.AnnaPetrosyan];

        var responses = await Task.WhenAll(members.Select(member => Reserve(Librarian(), 45, member)));

        var positions = new List<int>();
        foreach (var response in responses)
            positions.Add((await Created(response)).QueuePosition);
        Assert.Equal([1, 2, 3], positions.Order());
    }

    [Fact]
    public async Task Cancellation_enforces_ownership_compacts_queue_and_transfers_held_copy()
    {
        const int book = 57;
        Guid copyId = CatalogSeedIds.Copy(book, 1);
        Guid loanId = await Db(async db =>
        {
            var copy = await db.BookCopies.SingleAsync(item => item.Id == copyId);
            copy.ChangeStatus(BookCopyStatus.OnLoan);

            Loan loan = Loan.Issue(
                Guid.NewGuid(), copyId, Members.LilitGrigoryan,
                DateTime.UtcNow.AddDays(-1), 14);
            db.Loans.Add(loan);
            await db.SaveChangesAsync();
            return loan.Id;
        });

        using var librarian = Librarian();
        var first = await Created(await Reserve(librarian, book, Members.MariamAvetisyan));
        var second = await Created(await Reserve(librarian, book, Members.DavitHakobyan));
        var third = await Created(await Reserve(librarian, book, Members.AnnaPetrosyan));

        await AssertProblem(
            await Reader(Members.GorMkrtchyan).PostAsync($"/api/reservations/{second.ReservationId}/cancel", null),
            HttpStatusCode.NotFound,
            "borrowing.reservation_not_found");

        using (var owner = Reader(Members.DavitHakobyan))
        using (var response = await owner.PostAsync($"/api/reservations/{second.ReservationId}/cancel", null))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var cancelled = (await response.Content.ReadFromJsonAsync<ReservationDto>(Json))!;
            Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
            Assert.Equal(0, cancelled.QueuePosition);
        }

        await AssertProblem(
            await librarian.PostAsync($"/api/reservations/{second.ReservationId}/cancel", null),
            HttpStatusCode.UnprocessableEntity,
            BorrowingErrors.ReservationCannotBeCancelled.Code);

        Assert.Equal(1, (await Read(librarian, first.ReservationId)).QueuePosition);
        Assert.Equal(2, (await Read(librarian, third.ReservationId)).QueuePosition);

        // A librarian may cancel another person's reservation; the remaining queue is compacted again.
        using (var response = await librarian.PostAsync($"/api/reservations/{first.ReservationId}/cancel", null))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await Read(librarian, third.ReservationId)).QueuePosition);

        var fourth = await Created(await Reserve(librarian, book, Members.AramSargsyan));
        var fifth = await Created(await Reserve(librarian, book, Members.MariamAvetisyan));

        using (var returned = await librarian.PostAsJsonAsync(
                   $"/api/loans/{loanId}/return", new ReturnLoanRequest(Damaged: false), Json))
            Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        ReservationDto held = await Read(librarian, third.ReservationId);
        Assert.Equal(ReservationStatus.ReadyForPickup, held.Status);
        Assert.Equal(copyId, held.CopyId);
        var fourthState = await Read(librarian, fourth.ReservationId);
        Assert.Equal((ReservationStatus.Waiting, 1), (fourthState.Status, fourthState.QueuePosition));

        using (var response = await librarian.PostAsync($"/api/reservations/{held.ReservationId}/cancel", null))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var transferred = await Read(librarian, fourth.ReservationId);
        Assert.Equal(ReservationStatus.ReadyForPickup, transferred.Status);
        Assert.Equal(copyId, transferred.CopyId);
        var fifthState = await Read(librarian, fifth.ReservationId);
        Assert.Equal((ReservationStatus.Waiting, 1), (fifthState.Status, fifthState.QueuePosition));
        Assert.Equal(BookCopyStatus.Reserved, await Db(db => db.BookCopies
            .Where(item => item.Id == copyId).Select(item => item.Status).SingleAsync()));

        using (var response = await librarian.PostAsync($"/api/reservations/{transferred.ReservationId}/cancel", null))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var finalHolder = await Read(librarian, fifth.ReservationId);
        Assert.Equal(ReservationStatus.ReadyForPickup, finalHolder.Status);
        Assert.Equal(copyId, finalHolder.CopyId);

        using var otherLibrarian = Librarian();
        var cancellationResponses = await Task.WhenAll(
            librarian.PostAsync($"/api/reservations/{finalHolder.ReservationId}/cancel", null),
            otherLibrarian.PostAsync($"/api/reservations/{finalHolder.ReservationId}/cancel", null));
        try
        {
            Assert.Equal(
                new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity },
                cancellationResponses.Select(response => response.StatusCode).OrderBy(status => (int)status));
        }
        finally
        {
            foreach (HttpResponseMessage response in cancellationResponses)
                response.Dispose();
        }
        Assert.Equal(BookCopyStatus.Available, await Db(db => db.BookCopies
            .Where(item => item.Id == copyId).Select(item => item.Status).SingleAsync()));
    }
}
