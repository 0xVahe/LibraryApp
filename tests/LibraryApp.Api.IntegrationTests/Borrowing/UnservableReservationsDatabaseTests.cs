using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Jobs.Borrowing;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// Reservations that can never be served, on SQL Server through the real host: a book deleted through the Catalog endpoint (BR-CAT-3) and a
/// member deactivated through the Staff endpoint (BR-MEM-4) leave reservations that can never be served; the next
/// Borrowing daily job cancels them, frees the member's places (BR-BOR-2) and hands a held copy to the next member.
/// </summary>
public class UnservableReservationsDatabaseTests(BorrowingDailyJobHost factory) : IClassFixture<BorrowingDailyJobHost>
{
    [Fact]
    public async Task Waiting_reservations_of_a_book_deleted_in_catalog_are_cancelled_by_the_next_job()
    {
        Guid bookId = CatalogSeedIds.Book(50);

        // A book may be deleted once no copy is OnLoan or Reserved (BR-CAT-3): every copy is withdrawn first.
        using (HttpClient librarian = Client(StaffRoles.Librarian))
        {
            foreach (Guid copyId in await CopiesInCirculationAsync(bookId))
            {
                using HttpResponseMessage withdrawn = await librarian.PostAsJsonAsync(
                    $"/api/books/{bookId}/copies/{copyId}/status",
                    new ChangeCopyStatusRequest(BookCopyStatus.Withdrawn));
                Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
            }
        }

        // With no copy Available, a member reserves the book through the API (BR-BOR-3); a second one waits behind.
        // The StaffMember test account is Anna Petrosyan (TestAccounts).
        Guid anna = Members.AnnaPetrosyan;
        using (HttpClient member = Client(StaffRoles.StaffMember))
        {
            using HttpResponseMessage reserved = await member.PostAsJsonAsync(
                "/api/reservations", new CreateReservationRequest(bookId, null));
            Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        }

        Reservation second = await AddWaitingAsync(bookId, Members.MariamAvetisyan, queuePosition: 2);
        int annaActiveBefore = await ActiveReservationsAsync(anna);

        using (HttpClient librarian = Client(StaffRoles.Librarian))
        {
            using HttpResponseMessage deleted = await librarian.DeleteAsync($"/api/books/{bookId}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        string summary = await RunJobAsync();

        List<Reservation> queue = await Db(db => db.Reservations.AsNoTracking()
            .Where(reservation => reservation.BookId == bookId)
            .ToListAsync());

        Assert.Equal(2, queue.Count);
        Assert.All(queue, reservation =>
        {
            Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
            Assert.Equal(0, reservation.QueuePosition);
        });
        Assert.Contains(queue, reservation => reservation.Id == second.Id);

        // BR-BOR-2: the cancelled reservation no longer takes one of the member's places.
        Assert.Equal(annaActiveBefore - 1, await ActiveReservationsAsync(anna));
        Assert.Contains("failures: 0", summary);

        // A second run finds nothing more to do.
        string again = await RunJobAsync();
        Assert.Contains("cancelled reservations: 0", again);
    }

    [Fact]
    public async Task A_copy_held_for_a_member_deactivated_in_staff_goes_to_the_next_member_at_the_next_job()
    {
        Guid bookId = CatalogSeedIds.Book(51);
        Guid copyId = CatalogSeedIds.Copy(51, 1);
        Guid deactivated = Members.DavitHakobyan;
        DateTime now = factory.Services.GetRequiredService<IClock>().UtcNow;

        Reservation held = Reservation.Create(Guid.NewGuid(), bookId, deactivated, now.AddDays(-3), queuePosition: 1);
        held.MarkReady(copyId, now.AddDays(2));
        Reservation next = Reservation.Create(Guid.NewGuid(), bookId, Members.NarekSimonyan, now.AddDays(-2), queuePosition: 1);
        Reservation behind = Reservation.Create(Guid.NewGuid(), bookId, Members.EvaHovsepyan, now.AddDays(-1), queuePosition: 2);

        // The same member also waits for another book: that queue closes its gap.
        Guid otherBook = CatalogSeedIds.Book(52);
        Reservation otherWaiting = Reservation.Create(Guid.NewGuid(), otherBook, deactivated, now.AddDays(-2), queuePosition: 1);
        Reservation otherBehind = Reservation.Create(Guid.NewGuid(), otherBook, Members.TigranMelikyan, now.AddDays(-1), queuePosition: 2);

        await Db(async db =>
        {
            (await db.BookCopies.SingleAsync(copy => copy.Id == copyId)).ChangeStatus(BookCopyStatus.Reserved);
            db.Reservations.AddRange(held, next, behind, otherWaiting, otherBehind);
            return await db.SaveChangesAsync();
        });

        // BR-MEM-4: deactivation is blocked only by loans, so it succeeds while the member holds reservations.
        using (HttpClient admin = Client(StaffRoles.LibraryAdministrator))
        {
            using HttpResponseMessage response = await admin.PostAsJsonAsync(
                $"/api/staff-members/{deactivated}/deactivate", new DeactivateStaffMemberRequest("Left the bank"));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        int readyEventsBefore = await ReadyEventsAsync();

        string summary = await RunJobAsync();

        Reservation savedHeld = await ReadAsync(held.Id);
        Reservation savedNext = await ReadAsync(next.Id);
        Reservation savedBehind = await ReadAsync(behind.Id);

        Assert.Equal(ReservationStatus.Cancelled, savedHeld.Status);
        Assert.Equal((ReservationStatus.ReadyForPickup, (Guid?)copyId), (savedNext.Status, savedNext.CopyId));
        Assert.Equal((ReservationStatus.Waiting, 1), (savedBehind.Status, savedBehind.QueuePosition));
        Assert.Equal(BookCopyStatus.Reserved, await Db(db => db.BookCopies.AsNoTracking()
            .Where(copy => copy.Id == copyId).Select(copy => copy.Status).SingleAsync()));

        Assert.Equal(ReservationStatus.Cancelled, (await ReadAsync(otherWaiting.Id)).Status);
        Assert.Equal((ReservationStatus.Waiting, 1), ((await ReadAsync(otherBehind.Id)).Status, (await ReadAsync(otherBehind.Id)).QueuePosition));

        Assert.Equal(0, await ActiveReservationsAsync(deactivated));
        Assert.Equal(readyEventsBefore + 1, await ReadyEventsAsync());
        Assert.Contains("failures: 0", summary);

        // A second run hands nothing over again.
        await RunJobAsync();
        Assert.Equal(readyEventsBefore + 1, await ReadyEventsAsync());
        Assert.Equal(ReservationStatus.ReadyForPickup, (await ReadAsync(next.Id)).Status);
    }

    private async Task<string> RunJobAsync()
    {
        using var scope = factory.Services.CreateScope();
        DateTime now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var job = scope.ServiceProvider.GetRequiredService<BorrowingDailyJob>();
        return await job.RunAsync(new DailyJobRun(BorrowingDailyJob.Name, DateOnly.FromDateTime(now), now), CancellationToken.None);
    }

    private Task<List<Guid>> CopiesInCirculationAsync(Guid bookId) => Db(db => db.BookCopies.AsNoTracking()
        .Where(copy => copy.BookId == bookId
                       && copy.Status != BookCopyStatus.Lost
                       && copy.Status != BookCopyStatus.Withdrawn)
        .Select(copy => copy.Id)
        .ToListAsync());

    private async Task<Reservation> AddWaitingAsync(Guid bookId, Guid staffMemberId, int queuePosition)
    {
        DateTime now = factory.Services.GetRequiredService<IClock>().UtcNow;
        Reservation reservation = Reservation.Create(Guid.NewGuid(), bookId, staffMemberId, now, queuePosition);
        await Db(async db =>
        {
            db.Reservations.Add(reservation);
            return await db.SaveChangesAsync();
        });
        return reservation;
    }

    private Task<int> ActiveReservationsAsync(Guid staffMemberId) => Db(db => db.Reservations.AsNoTracking()
        .CountAsync(reservation => reservation.StaffMemberId == staffMemberId
                                   && (reservation.Status == ReservationStatus.Waiting
                                       || reservation.Status == ReservationStatus.ReadyForPickup)));

    private Task<Reservation> ReadAsync(Guid reservationId) =>
        Db(db => db.Reservations.AsNoTracking().SingleAsync(reservation => reservation.Id == reservationId));

    private async Task<int> ReadyEventsAsync() =>
        (await Db(db => db.OutboxMessages.AsNoTracking().Select(message => message.Type).ToListAsync()))
        .Count(type => type.EndsWith("ReservationReady", StringComparison.Ordinal));

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    private HttpClient Client(string role)
    {
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(factory.Account(role)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
