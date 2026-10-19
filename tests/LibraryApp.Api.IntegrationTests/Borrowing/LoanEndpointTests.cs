using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// Issuing a loan (BOR-01, BOR-02) against the real host and database with the Development seed: seeded staff members
/// and seeded copies (CatalogSeedIds). Every test uses its own staff member and copies, so the tests of the class do
/// not depend on each other.
/// </summary>
public class LoanEndpointTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    // As the API writes JSON: camelCase, enums as names (SD-11).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private HttpClient Client(string? role = StaffRoles.Librarian)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                factory.Account(role)).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static Task<HttpResponseMessage> Issue(HttpClient client, Guid staffMemberId, string barcode) =>
        client.PostAsJsonAsync("/api/loans", new IssueLoanRequest(staffMemberId, barcode));

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    private Task<BookCopyStatus> CopyStatus(int book, int copy) =>
        Db(db => db.BookCopies.Where(x => x.Id == CatalogSeedIds.Copy(book, copy)).Select(x => x.Status).SingleAsync());

    [Fact]
    public async Task A_librarian_issues_a_copy_and_reads_the_loan_back()
    {
        using var client = Client();
        var response = await Issue(client, Members.MariamAvetisyan, " " + CatalogSeedIds.CopyBarcode(2, 1).ToLowerInvariant() + " ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var loan = (await response.Content.ReadFromJsonAsync<LoanDto>(Json))!;
        Assert.Equal(Members.MariamAvetisyan, loan.StaffMemberId);
        Assert.Equal(CatalogSeedIds.Copy(2, 1), loan.BookCopyId);
        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Equal(30, (loan.DueAtUtc - loan.IssuedAtUtc).TotalDays);   // Manager / Senior Specialist: 30 days
        Assert.Equal(BookCopyStatus.OnLoan, await CopyStatus(2, 1));

        Assert.Equal(loan, await client.GetFromJsonAsync<LoanDto>(response.Headers.Location, Json));
    }

    [Fact]
    public async Task LoanIssued_is_stored_in_the_outbox_with_the_book()
    {
        using var client = Client();
        var loan = (await (await Issue(client, Members.MariamAvetisyan, CatalogSeedIds.CopyBarcode(3, 1)))
            .Content.ReadFromJsonAsync<LoanDto>(Json))!;

        var message = await Db(db => db.Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(m => m.Type.Contains(nameof(LoanIssued)) && m.Payload.Contains(loan.LoanId.ToString())));
        var issued = JsonSerializer.Deserialize<LoanIssued>(message.Payload, Json)!;
        Assert.Equal(loan.LoanId, issued.LoanId);
        Assert.Equal(CatalogSeedIds.Book(3), issued.BookId);
        Assert.Equal(loan.DueAtUtc, issued.DueAtUtc);
    }

    [Fact]
    public async Task A_member_collects_the_copy_held_for_their_reservation()
    {
        const int book = 57;
        Guid copyId = CatalogSeedIds.Copy(book, 1);
        // Use members not shared by the other loan tests: this fixture runs tests against one database in parallel.
        Guid holder = Members.ArmenBabayan;
        Loan existingLoan = Loan.Issue(Guid.NewGuid(), copyId, Members.ErikZakaryan, DateTime.UtcNow.AddDays(-1), 14);
        await Db(async db =>
        {
            db.BookCopies.Single(copy => copy.Id == copyId).ChangeStatus(BookCopyStatus.OnLoan);
            db.Loans.Add(existingLoan);
            return await db.SaveChangesAsync();
        });

        using var client = Client();
        using var reserved = await client.PostAsJsonAsync("/api/reservations",
            new CreateReservationRequest(CatalogSeedIds.Book(book), holder));
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        ReservationDto reservation = (await reserved.Content.ReadFromJsonAsync<ReservationDto>(Json))!;

        using var returned = await client.PostAsJsonAsync($"/api/loans/{existingLoan.Id}/return", new ReturnLoanRequest(Damaged: false));
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Reservation ready = await Db(db => db.Reservations.AsNoTracking().SingleAsync(item => item.Id == reservation.ReservationId));
        Assert.Equal(ReservationStatus.ReadyForPickup, ready.Status);
        Assert.Equal(copyId, ready.CopyId);
        Assert.Equal(BookCopyStatus.Reserved, await CopyStatus(book, 1));

        using var issued = await Issue(client, holder, CatalogSeedIds.CopyBarcode(book, 1));

        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        LoanDto newLoan = (await issued.Content.ReadFromJsonAsync<LoanDto>(Json))!;
        Assert.Equal(holder, newLoan.StaffMemberId);
        Assert.Equal(LoanStatus.Active, newLoan.Status);
        Reservation fulfilled = await Db(db => db.Reservations.AsNoTracking().SingleAsync(item => item.Id == reservation.ReservationId));
        Assert.Equal(ReservationStatus.Fulfilled, fulfilled.Status);
        Assert.Equal(BookCopyStatus.OnLoan, await CopyStatus(book, 1));
    }

    /// <summary>Copy 1 of the book is held for <paramref name="holder"/> (ReadyForPickup); the others wait in this order.</summary>
    private async Task<Guid> HoldCopyAsync(int book, Guid holder, params Guid[] waiting)
    {
        var ready = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(book), holder, DateTime.UtcNow.AddHours(-2), 1);
        ready.MarkReady(CatalogSeedIds.Copy(book, 1), DateTime.UtcNow.AddDays(3));
        await Db(async db =>
        {
            db.BookCopies.Single(copy => copy.Id == CatalogSeedIds.Copy(book, 1)).ChangeStatus(BookCopyStatus.Reserved);
            db.Reservations.Add(ready);
            int position = 1;
            foreach (Guid member in waiting)
                db.Reservations.Add(Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(book), member, DateTime.UtcNow.AddHours(-1), position++));
            return await db.SaveChangesAsync();
        });
        return ready.Id;
    }

    /// <summary>
    /// BQ-BA-08: any copy of the book fulfils the holder's reservation; the held copy goes to the next in the queue. Both
    /// orders of the two reservation ids: EF writes the two updates in key order, and the database allows one
    /// ReadyForPickup reservation per copy, so the fulfilled one must be written first whatever the ids are.
    /// </summary>
    [Theory]
    [InlineData(13, true)]
    [InlineData(18, false)]
    public async Task Issuing_another_copy_to_the_holder_hands_the_held_copy_to_the_next_member(int book, bool holderIdFirst)
    {
        Guid low = Guid.Parse($"00000000-0000-0000-0000-{book:D12}"), high = Guid.Parse($"ffffffff-ffff-ffff-ffff-{book:D12}");
        (Guid holder, Guid next) = book == 13
            ? (Members.NarekSimonyan, Members.EvaHovsepyan)
            : (Members.LusineKarapetyan, Members.RubenHarutyunyan);
        var ready = Reservation.Create(holderIdFirst ? low : high, CatalogSeedIds.Book(book), holder, DateTime.UtcNow.AddHours(-2), 1);
        ready.MarkReady(CatalogSeedIds.Copy(book, 1), DateTime.UtcNow.AddDays(3));
        var waiting = Reservation.Create(holderIdFirst ? high : low, CatalogSeedIds.Book(book), next, DateTime.UtcNow.AddHours(-1), 1);
        await Db(async db =>
        {
            db.BookCopies.Single(copy => copy.Id == CatalogSeedIds.Copy(book, 1)).ChangeStatus(BookCopyStatus.Reserved);
            db.Reservations.AddRange(ready, waiting);
            return await db.SaveChangesAsync();
        });

        using var client = Client();
        using var response = await Issue(client, holder, CatalogSeedIds.CopyBarcode(book, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reservations = await Db(db => db.Reservations.AsNoTracking().Where(r => r.BookId == CatalogSeedIds.Book(book)).ToListAsync());
        Assert.Equal(ReservationStatus.Fulfilled, reservations.Single(r => r.Id == ready.Id).Status);
        Reservation handedOn = reservations.Single(r => r.Id == waiting.Id);
        Assert.Equal((ReservationStatus.ReadyForPickup, CatalogSeedIds.Copy(book, 1)), (handedOn.Status, handedOn.CopyId));
        Assert.Equal(BookCopyStatus.Reserved, await CopyStatus(book, 1));
        Assert.Equal(BookCopyStatus.OnLoan, await CopyStatus(book, 2));
        Assert.True(await Db(db => db.OutboxMessages.AnyAsync(m => m.Type.Contains(nameof(ReservationReady)) && m.Payload.Contains(waiting.Id.ToString()))));
    }

    [Fact]
    public async Task Issuing_another_copy_to_the_holder_releases_the_held_copy_when_nobody_waits()
    {
        Guid readyId = await HoldCopyAsync(14, Members.TigranMelikyan);

        using var client = Client();
        using var response = await Issue(client, Members.TigranMelikyan, CatalogSeedIds.CopyBarcode(14, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ReservationStatus.Fulfilled, await Db(db => db.Reservations.Where(r => r.Id == readyId).Select(r => r.Status).SingleAsync()));
        Assert.Equal(BookCopyStatus.Available, await CopyStatus(14, 1));
    }

    [Fact]
    public async Task Issuing_a_copy_to_a_waiting_member_cancels_their_reservation_and_moves_the_queue_up()
    {
        var own = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(1), Members.EvaHovsepyan, DateTime.UtcNow.AddHours(-2), 1);
        var behind = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(1), Members.ManeGevorgyan, DateTime.UtcNow.AddHours(-1), 2);
        await Db(async db =>
        {
            db.Reservations.AddRange(own, behind);
            return await db.SaveChangesAsync();
        });

        using var client = Client();
        using var response = await Issue(client, Members.EvaHovsepyan, CatalogSeedIds.CopyBarcode(1, 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await Db(db => db.Reservations.AsNoTracking().Where(r => r.Id == own.Id || r.Id == behind.Id).ToListAsync());
        Assert.Equal(ReservationStatus.Cancelled, stored.Single(r => r.Id == own.Id).Status);
        Assert.Equal((ReservationStatus.Waiting, 1), (stored.Single(r => r.Id == behind.Id).Status, stored.Single(r => r.Id == behind.Id).QueuePosition));
    }

    [Fact]
    public async Task A_held_copy_is_refused_to_another_member_and_stays_held()
    {
        Guid readyId = await HoldCopyAsync(7, Members.ManeGevorgyan);

        using var client = Client();
        await AssertProblem(await Issue(client, Members.EvaHovsepyan, CatalogSeedIds.CopyBarcode(7, 1)),
            HttpStatusCode.UnprocessableEntity, "borrowing.copy_not_available");

        Assert.Equal(ReservationStatus.ReadyForPickup, await Db(db => db.Reservations.Where(r => r.Id == readyId).Select(r => r.Status).SingleAsync()));
        Assert.Equal(BookCopyStatus.Reserved, await CopyStatus(7, 1));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    public async Task Only_librarians_and_administrators_issue_and_read(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);

        Assert.Equal(expected, (await Issue(client, Members.AnnaPetrosyan, CatalogSeedIds.CopyBarcode(4, 1))).StatusCode);
        Assert.Equal(expected, (await client.GetAsync($"/api/loans/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(BookCopyStatus.Available, await CopyStatus(4, 1));
    }

    [Fact]
    public async Task Invalid_input_is_400_with_errors_per_field()
    {
        using var client = Client();
        var response = await Issue(client, Guid.Empty, "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(nameof(IssueLoanRequest.StaffMemberId), out _));
        Assert.True(errors.TryGetProperty(nameof(IssueLoanRequest.Barcode), out _));
    }

    [Fact]
    public async Task An_unknown_barcode_is_404_and_an_unknown_loan_is_404()
    {
        using var client = Client();

        await AssertProblem(await Issue(client, Members.MariamAvetisyan, "BK-9999-01"), HttpStatusCode.NotFound, "borrowing.copy_not_found");
        await AssertProblem(await client.GetAsync($"/api/loans/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "borrowing.loan_not_found");
    }

    [Fact]
    public async Task A_copy_already_on_loan_is_422()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Created, (await Issue(client, Members.MariamAvetisyan, CatalogSeedIds.CopyBarcode(5, 1))).StatusCode);

        await AssertProblem(await Issue(client, Members.DavitHakobyan, CatalogSeedIds.CopyBarcode(5, 1)),
            HttpStatusCode.UnprocessableEntity, "borrowing.copy_not_available");
    }

    [Fact]
    public async Task A_suspended_staff_member_is_refused_with_the_reason()
    {
        using var client = Client();

        await AssertProblem(await Issue(client, Members.SonaVardanyan, CatalogSeedIds.CopyBarcode(6, 1)),
            HttpStatusCode.UnprocessableEntity, StaffErrors.Suspended.Code);
        Assert.Equal(BookCopyStatus.Available, await CopyStatus(6, 1));
    }

    /// <summary>
    /// Four issues for one employee at the same time (Employee: 3 loans). The staff lock (BorrowingLocks.Staff) makes
    /// them run one after the other, so exactly one is refused with the agreed limit message.
    /// </summary>
    [Fact]
    public async Task Parallel_issues_never_pass_the_loan_limit()
    {
        using var client = Client();
        int[] books = [8, 9, 10, 11];

        var responses = await Task.WhenAll(books.Select(book => Issue(client, Members.AnnaPetrosyan, CatalogSeedIds.CopyBarcode(book, 1))));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var refused = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("staff.loan_limit_reached", body.GetProperty("code").GetString());
        Assert.Equal("Loan limit reached (3/3).", body.GetProperty("detail").GetString());
        Assert.Equal(3, await Db(db => db.Loans.CountAsync(l => l.StaffMemberId == Members.AnnaPetrosyan && l.Status == LoanStatus.Active)));
    }

    /// <summary>
    /// BR-BOR-4 on the real database: the save fails after the copy was set OnLoan (an open loan of the copy already
    /// exists, so the unique index refuses the new one). Nothing is kept: the copy stays Available, no event is stored.
    /// </summary>
    [Fact]
    public async Task A_failed_save_keeps_the_copy_available_and_stores_no_event()
    {
        var copyId = CatalogSeedIds.Copy(12, 1);
        var existing = Loan.Issue(Guid.NewGuid(), copyId, Members.DavitHakobyan, DateTime.UtcNow.AddDays(-1), 14);
        await Db(async db => { db.Loans.Add(existing); return await db.SaveChangesAsync(); });
        int eventsBefore = await Db(db => db.Set<OutboxMessage>().CountAsync());

        using var client = Client();
        await AssertProblem(await Issue(client, Members.MariamAvetisyan, CatalogSeedIds.CopyBarcode(12, 1)),
            HttpStatusCode.Conflict, "unique_violation");

        Assert.Equal(BookCopyStatus.Available, await CopyStatus(12, 1));
        Assert.Equal(1, await Db(db => db.Loans.CountAsync(l => l.BookCopyId == copyId)));
        Assert.Equal(eventsBefore, await Db(db => db.Set<OutboxMessage>().CountAsync()));
    }
}
