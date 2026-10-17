using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The real Borrowing read contract for Staff (BR-MEM-4) on the host with the Development seed: the registered
/// provider, the open loans it returns, and Staff's deactivation refused because of them.
/// </summary>
public class BorrowingReadServiceTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    /// <summary>An Active loan (first book), an Overdue one (second book) and a returned one (third book, not open).</summary>
    private async Task<(Loan Active, Loan Overdue)> SeedLoansAsync(Guid member, int activeBook, int overdueBook, int returnedBook)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var active = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(activeBook, 1), member, Now.AddDays(-2), 14);
        var overdue = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(overdueBook, 1), member, Now.AddDays(-30), 14);
        overdue.MarkOverdue(Now);
        var returned = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(returnedBook, 1), member, Now.AddDays(-40), 14);
        returned.Return(Now.AddDays(-30));

        db.Loans.AddRange(active, overdue, returned);
        await db.SaveChangesAsync();
        return (active, overdue);
    }

    [Fact]
    public async Task The_application_uses_the_real_provider_and_returns_only_open_loans_with_their_books()
    {
        var (active, overdue) = await SeedLoansAsync(Members.DavitHakobyan, 13, 14, 7);

        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IBorrowingReadService>();
        Assert.IsType<BorrowingReadService>(reader);

        var loans = await reader.GetActiveLoansAsync(Members.DavitHakobyan);

        // Earliest due date first; the returned loan is not open.
        Assert.Equal(
            [
                new OpenLoanInfo(overdue.Id, Members.DavitHakobyan, CatalogSeedIds.Copy(14, 1), CatalogSeedIds.Book(14), overdue.DueAtUtc, LoanStatus.Overdue,
                    overdue.IssuedAtUtc, 0),
                new OpenLoanInfo(active.Id, Members.DavitHakobyan, CatalogSeedIds.Copy(13, 1), CatalogSeedIds.Book(13), active.DueAtUtc, LoanStatus.Active,
                    active.IssuedAtUtc, 0),
            ],
            loans);
        Assert.Empty(await reader.GetActiveLoansAsync(Members.MariamAvetisyan));
    }

    [Fact]
    public async Task Deactivating_a_staff_member_with_open_loans_is_refused_with_the_outstanding_books()
    {
        await SeedLoansAsync(Members.GorMkrtchyan, 16, 17, 18);
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
            new LoginAccount(Guid.NewGuid(), "admin@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active,
                [StaffRoles.LibraryAdministrator])).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync($"/api/staff-members/{Members.GorMkrtchyan}/deactivate",
            new DeactivateStaffMemberRequest("Left the bank"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("staff.has_active_loans", body.GetProperty("code").GetString());
        var barcodes = body.GetProperty("outstandingBooks").EnumerateArray()
            .Select(book => book.GetProperty("barcode").GetString()).ToHashSet();
        Assert.Equal([CatalogSeedIds.CopyBarcode(16, 1), CatalogSeedIds.CopyBarcode(17, 1)], barcodes.Order());
    }

    [Fact]
    public async Task My_library_has_the_open_loans_open_reservations_and_unexpired_active_penalties()
    {
        Guid member = Members.AramSargsyan;
        var renewed = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(23, 1), member, Now.AddDays(-10), 21);
        renewed.Renew(21);
        var returned = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(33, 1), member, Now.AddDays(-120), 21);
        returned.Return(Now.AddDays(-100));

        var waiting = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(24), member, Now.AddDays(-3), 1);
        var ready = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(25), member, Now.AddDays(-2), 1);
        ready.MarkReady(CatalogSeedIds.Copy(25, 1), Now.AddDays(1));
        var cancelled = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(26), member, Now.AddDays(-4), 1);
        cancelled.Cancel();

        var older = PenaltyRecord.Create(Guid.NewGuid(), member, returned.Id, 3, "Returned late", Now.AddDays(-10), Now.AddDays(-10).AddDays(90));
        var newer = PenaltyRecord.Create(Guid.NewGuid(), member, renewed.Id, 1, "Returned damaged", Now.AddDays(-1), Now.AddDays(-1).AddDays(90));
        var waived = PenaltyRecord.Create(Guid.NewGuid(), member, returned.Id, 2, "Returned late", Now.AddDays(-5), Now.AddDays(-5).AddDays(90));
        waived.Waive("Business trip");
        var timeExpired = PenaltyRecord.Create(Guid.NewGuid(), member, returned.Id, 5, "Returned late", Now.AddDays(-100), Now.AddDays(-100).AddDays(90));

        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.Loans.AddRange(renewed, returned);
            db.Reservations.AddRange(waiting, ready, cancelled);
            db.PenaltyRecords.AddRange(older, newer, waived, timeExpired);
            await db.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var summary = await scope.ServiceProvider.GetRequiredService<IBorrowingReadService>().GetStaffSummaryAsync(member);

        Assert.Equal(member, summary.StaffMemberId);
        var loan = Assert.Single(summary.ActiveLoans);
        Assert.Equal((renewed.Id, CatalogSeedIds.Book(23), 1, renewed.IssuedAtUtc, renewed.DueAtUtc),
            (loan.LoanId, loan.BookId, loan.RenewalCount, loan.IssuedAtUtc, loan.DueAtUtc));
        Assert.Equal(
            [
                new OpenReservationInfo(waiting.Id, CatalogSeedIds.Book(24), ReservationStatus.Waiting, 1, null, waiting.CreatedAtUtc),
                new OpenReservationInfo(ready.Id, CatalogSeedIds.Book(25), ReservationStatus.ReadyForPickup, 0, ready.ReadyUntilUtc, ready.CreatedAtUtc),
            ],
            summary.Reservations);
        Assert.Equal(
            [
                // The book of a returned loan, which is not in ActiveLoans.
                new ActivePenaltyInfo(older.Id, returned.Id, CatalogSeedIds.Book(33), 3, "Returned late", older.CreatedAtUtc, older.ExpiresAtUtc),
                new ActivePenaltyInfo(newer.Id, renewed.Id, CatalogSeedIds.Book(23), 1, "Returned damaged", newer.CreatedAtUtc, newer.ExpiresAtUtc),
            ],
            summary.ActivePenalties);
        Assert.Equal(4, summary.ActivePenaltyPoints);
    }

    [Fact]
    public async Task Loans_due_on_a_local_date_include_both_ends_of_that_day_and_only_open_loans()
    {
        using var scope = factory.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var day = clock.LocalToday.AddDays(40);
        DateTime start = clock.StartOfLocalDayUtc(day), end = clock.EndOfLocalDayUtc(day);

        Loan DueAt(int book, DateTime dueAtUtc) =>
            Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(book, 1), Members.LilitGrigoryan, dueAtUtc.AddDays(-14), 14);
        var first = DueAt(27, start);
        var last = DueAt(28, end);
        var nextDay = DueAt(30, end.AddTicks(1));
        var previousDay = DueAt(31, start.AddTicks(-1));
        var returned = DueAt(32, start.AddHours(10));
        returned.Return(start.AddDays(-1));

        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        db.Loans.AddRange(first, last, nextDay, previousDay, returned);
        await db.SaveChangesAsync();

        var due = await scope.ServiceProvider.GetRequiredService<IBorrowingReadService>().GetLoansDueOnAsync(day);

        Assert.Equal([first.Id, last.Id], due.Select(loan => loan.LoanId));
    }
}
