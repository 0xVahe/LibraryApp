using System.Net;
using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// G3-19: the Loan desk <c>/desk</c> by the UI guide (Part 2 "Loan desk", demo <c>desk.html</c>, corrections of the UI guide
/// check): issue by employee ID or email and barcode (BOR-01), take back by barcode with "Damaged — send to repair" and a
/// note (BOR-03, BOR-05, BOR-09), the copies waiting for pickup (BOR-07). Every test uses its own members and books.
/// </summary>
public class LoanDeskPageTests(BorrowingUiHost factory) : IClassFixture<BorrowingUiHost>
{
    private const string Desk = "/desk";

    private static string EmployeeId(Guid member) => StaffSeedData.People.Single(p => p.StaffMemberId == member).EmployeeId;

    private static string FullName(Guid member) => StaffSeedData.People.Single(p => p.StaffMemberId == member) is var p ? $"{p.FirstName} {p.LastName}" : "";

    private Task<UiBrowser> LibrarianAsync() => UiBrowser.SignInAsync(factory, Members.AramSargsyan);

    private async Task<string> TitleAsync(int book)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ICatalogService>().GetBookAsync(CatalogSeedIds.Book(book)))!.Title;
    }

    private async Task<List<Loan>> LoansAsync(Guid member)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Loans.AsNoTracking()
            .Where(l => l.StaffMemberId == member).ToListAsync();
    }

    private async Task<BookCopyStatus> CopyStatusAsync(int book, int copy)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().BookCopies.AsNoTracking()
            .SingleAsync(c => c.Id == CatalogSeedIds.Copy(book, copy))).Status;
    }

    private async Task IssueAsync(Guid member, int book, int copy = 1)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ILoanService>()
            .IssueAsync(new IssueLoanRequest(member, CatalogSeedIds.CopyBarcode(book, copy)));
    }

    /// <summary>A loan issued <paramref name="daysAgo"/> days ago for 14 days, its copy on loan: overdue when it comes back.</summary>
    private async Task LateLoanAsync(Guid member, int book, int daysAgo)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        db.Loans.Add(Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(book, 1), member, clock.UtcNow.AddDays(-daysAgo), 14));
        (await db.BookCopies.SingleAsync(c => c.Id == CatalogSeedIds.Copy(book, 1))).ChangeStatus(BookCopyStatus.OnLoan);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_desk_has_the_issue_and_return_forms_and_the_pickup_table()
    {
        var (status, html, _) = await (await LibrarianAsync()).GetAsync(Desk);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<h1>Loan desk</h1>", html);
        Assert.Contains("<section class=\"card\" aria-labelledby=\"issue-title\">", html);
        Assert.Contains("<label for=\"issue-member\">Staff member</label>", html);
        Assert.Contains("<label for=\"issue-barcode\">Copy barcode</label>", html);
        Assert.Contains("<section class=\"card\" aria-labelledby=\"return-title\">", html);
        Assert.Contains("<label for=\"return-barcode\">Copy barcode</label>", html);
        Assert.Contains("Damaged — send to repair", html);
        Assert.Contains("<label for=\"return-note\">Note (optional)</label>", html);
        Assert.DoesNotContain("Worn", html);                       // the return knows only damaged or not
        Assert.Contains("<section class=\"card flush\" aria-labelledby=\"pickup-title\">", html);
        Assert.Contains("<a href=\"/desk\" aria-current=\"page\">Loan desk</a>", html);
        Assert.DoesNotContain("style=\"", html);
        Assert.DoesNotContain("class=\"owner", html);
    }

    [Theory]
    [InlineData("LibraryAdministrator")]
    [InlineData("Librarian")]
    public async Task Library_staff_open_the_desk(string role)
    {
        var browser = await UiBrowser.SignInAsync(factory, role == "Librarian" ? Members.AramSargsyan : Members.LilitGrigoryan);

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(Desk)).Status);
    }

    [Fact]
    public async Task A_staff_member_is_sent_to_the_forbidden_page_and_cannot_post()
    {
        var browser = await UiBrowser.SignInAsync(factory, Members.LusineKarapetyan);

        var (status, _, response) = await browser.GetAsync(Desk);
        Assert.Equal(HttpStatusCode.Redirect, status);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);

        var post = await browser.PostAsync("/forbidden", $"{Desk}?handler=Issue",
            ("Member", EmployeeId(Members.LusineKarapetyan)), ("Barcode", CatalogSeedIds.CopyBarcode(3, 1)));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Empty(await LoansAsync(Members.LusineKarapetyan));
    }

    [Fact]
    public async Task Anonymous_visitors_sign_in_first()
    {
        var (status, _, response) = await UiBrowser.Anonymous(factory).GetAsync(Desk);

        Assert.Equal(HttpStatusCode.Redirect, status);
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_book_is_issued_by_employee_id_or_corporate_email_BOR_01(bool byEmail)
    {
        Guid member = byEmail ? Members.RubenHarutyunyan : Members.AnahitMartirosyan;
        int book = byEmail ? 4 : 5;
        string who = byEmail
            ? "  " + StaffSeedData.People.Single(p => p.StaffMemberId == member).Email.ToUpperInvariant() + " "
            : EmployeeId(member);
        var browser = await LibrarianAsync();

        var response = await browser.PostAsync(Desk, $"{Desk}?handler=Issue", ("Member", who), ("Barcode", CatalogSeedIds.CopyBarcode(book, 1).ToLowerInvariant()));

        Assert.Equal(Desk, response.Headers.Location!.OriginalString);
        Loan loan = Assert.Single(await LoansAsync(member));
        Assert.Equal((CatalogSeedIds.Copy(book, 1), LoanStatus.Active), (loan.BookCopyId, loan.Status));

        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-green", next);
        Assert.Contains("<strong>Issued</strong>", next);
        using var scope = factory.Services.CreateScope();
        DateOnly due = scope.ServiceProvider.GetRequiredService<IClock>().ToLocalDate(loan.DueAtUtc);
        Assert.Contains(
            $"{Texts.Quoted(await TitleAsync(book))} ({CatalogSeedIds.CopyBarcode(book, 1)}) to {FullName(member)}. Due {Texts.Date(due)}.", next);
    }

    [Fact]
    public async Task Empty_fields_are_marked_next_to_the_inputs()
    {
        var response = await (await LibrarianAsync()).PostAsync(Desk, $"{Desk}?handler=Issue", ("Member", " "), ("Barcode", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await UiBrowser.HtmlAsync(response);
        Assert.Contains("<span class=\"error-text\">Enter the employee ID or corporate email.</span>", html);
        Assert.Contains("<span class=\"error-text\">Enter the copy barcode.</span>", html);
        Assert.Matches("<input id=\"issue-member\"[^>]*aria-invalid=\"true\"", html);
        Assert.Matches("<input id=\"issue-barcode\"[^>]*aria-invalid=\"true\"", html);
    }

    [Fact]
    public async Task An_unknown_member_is_a_field_error_and_the_form_stays_filled()
    {
        var response = await (await LibrarianAsync()).PostAsync(Desk, $"{Desk}?handler=Issue", ("Member", "EMP-0000"), ("Barcode", CatalogSeedIds.CopyBarcode(6, 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await UiBrowser.HtmlAsync(response);
        Assert.Contains("No staff member has this employee ID or corporate email.", html);
        Assert.Contains("value=\"EMP-0000\"", html);
        Assert.Contains($"value=\"{CatalogSeedIds.CopyBarcode(6, 1)}\"", html);
    }

    [Fact]
    public async Task A_refused_issue_shows_the_reason_in_the_issue_card()
    {
        var response = await (await LibrarianAsync()).PostAsync(Desk, $"{Desk}?handler=Issue",
            ("Member", EmployeeId(Members.ElenDanielyan)), ("Barcode", "BC-NO-SUCH-COPY"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await UiBrowser.HtmlAsync(response);
        Assert.Contains("<div class=\"alert alert-red mb-16\" role=\"alert\"><strong>The book was not issued.</strong>", html);
        Assert.Contains(BorrowingErrors.CopyNotFound.Message, html);
        Assert.Empty(await LoansAsync(Members.ElenDanielyan));
    }

    [Fact]
    public async Task A_book_taken_back_on_time_is_green_and_the_copy_is_available_BOR_03()
    {
        await IssueAsync(Members.SamvelPoghosyan, 7);
        var browser = await LibrarianAsync();

        var response = await browser.PostAsync(Desk, $"{Desk}?handler=Return",
            ("ReturnBarcode", $" {CatalogSeedIds.CopyBarcode(7, 1)} "), ("Damaged", "false"), ("Note", ""));

        Assert.Equal(Desk, response.Headers.Location!.OriginalString);
        Assert.Equal(LoanStatus.Returned, Assert.Single(await LoansAsync(Members.SamvelPoghosyan)).Status);
        Assert.Equal(BookCopyStatus.Available, await CopyStatusAsync(7, 1));

        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-green", next);
        Assert.Contains("<strong>Returned</strong>", next);
        Assert.Contains($"{Texts.Quoted(await TitleAsync(7))} ({CatalogSeedIds.CopyBarcode(7, 1)}) from {FullName(Members.SamvelPoghosyan)}.", next);
    }

    [Fact]
    public async Task A_damaged_copy_goes_to_repair_with_an_amber_alert_BOR_09()
    {
        await IssueAsync(Members.AniSahakyan, 8);
        var browser = await LibrarianAsync();

        var response = await browser.PostAsync(Desk, $"{Desk}?handler=Return",
            ("ReturnBarcode", CatalogSeedIds.CopyBarcode(8, 1)), ("Damaged", "true"), ("Damaged", "false"), ("Note", "Torn cover"));

        Assert.Equal(BookCopyStatus.UnderRepair, await CopyStatusAsync(8, 1));
        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-amber", next);
        Assert.Contains("<strong>Returned damaged</strong>", next);
        Assert.Contains("The copy goes to repair.", next);
    }

    [Fact]
    public async Task A_late_return_says_the_working_days_and_the_points_BOR_05()
    {
        await LateLoanAsync(Members.LianaNalbandyan, 9, daysAgo: 30);
        var browser = await LibrarianAsync();

        var response = await browser.PostAsync(Desk, $"{Desk}?handler=Return",
            ("ReturnBarcode", CatalogSeedIds.CopyBarcode(9, 1)), ("Damaged", "false"));

        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-amber", next);
        Assert.Matches(@"<strong>Returned \d+ working days? late</strong>", next);
        Assert.Matches(@"\d+ penalty points? added\.", next);
        Assert.Equal(LoanStatus.Returned, Assert.Single(await LoansAsync(Members.LianaNalbandyan)).Status);
    }

    [Theory]
    [InlineData("BC-NO-SUCH-COPY", "borrowing.copy_not_found")]
    [InlineData(null, "borrowing.no_open_loan_for_copy")]
    public async Task A_copy_that_cannot_be_taken_back_shows_the_reason_in_the_return_card(string? barcode, string code)
    {
        var response = await (await LibrarianAsync()).PostAsync(Desk, $"{Desk}?handler=Return",
            ("ReturnBarcode", barcode ?? CatalogSeedIds.CopyBarcode(10, 1)), ("Damaged", "false"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await UiBrowser.HtmlAsync(response);
        Assert.Contains("<strong>The book was not taken back.</strong>", html);
        var error = code == BorrowingErrors.CopyNotFound.Code ? BorrowingErrors.CopyNotFound : BorrowingErrors.NoOpenLoanForCopy;
        Assert.Contains(error.Message, html);
    }

    [Fact]
    public async Task A_note_longer_than_the_limit_is_a_field_error_and_nothing_is_returned()
    {
        await IssueAsync(Members.SofiaHambardzumyan, 11);

        var response = await (await LibrarianAsync()).PostAsync(Desk, $"{Desk}?handler=Return",
            ("ReturnBarcode", CatalogSeedIds.CopyBarcode(11, 1)), ("Damaged", "false"), ("Note", new string('x', Loan.ReturnNoteMaxLength + 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await UiBrowser.HtmlAsync(response);
        Assert.Contains($"The return note may have at most {Loan.ReturnNoteMaxLength} characters.", html);
        Assert.Contains("id=\"return-note\" name=\"Note\"", html);
        Assert.Equal(LoanStatus.Active, Assert.Single(await LoansAsync(Members.SofiaHambardzumyan)).Status);
    }

    [Fact]
    public async Task A_copy_held_for_pickup_is_listed_and_issued_from_the_table_BOR_07()
    {
        DateOnly holdUntil;
        Guid reservationId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var reservation = Reservation.Create(reservationId, CatalogSeedIds.Book(12), Members.AshotBaghdasaryan, clock.UtcNow.AddDays(-3), 1);
            DateTime until = clock.UtcNow.AddDays(2);
            reservation.MarkReady(CatalogSeedIds.Copy(12, 1), until);
            db.Reservations.Add(reservation);
            (await db.BookCopies.SingleAsync(c => c.Id == CatalogSeedIds.Copy(12, 1))).ChangeStatus(BookCopyStatus.Reserved);
            await db.SaveChangesAsync();
            holdUntil = clock.ToLocalDate(until);
        }
        var browser = await LibrarianAsync();

        var (_, html, _) = await browser.GetAsync(Desk);
        Assert.Contains($"<td data-label=\"Book\">{await TitleAsync(12)}</td>", html);
        Assert.Contains($"<a class=\"row-link\" href=\"/staff/{Members.AshotBaghdasaryan}\">{FullName(Members.AshotBaghdasaryan)}</a>", html);
        Assert.Contains($"<td class=\"nowrap\" data-label=\"Copy\">{CatalogSeedIds.CopyBarcode(12, 1)}</td>", html);
        Assert.Contains($"<td data-label=\"Hold until\">{Texts.Date(holdUntil)}</td>", html);

        var response = await browser.PostAsync(Desk, $"{Desk}?handler=IssueHeld",
            ("StaffMemberId", Members.AshotBaghdasaryan.ToString()), ("Barcode", CatalogSeedIds.CopyBarcode(12, 1)));

        Assert.Equal(Desk, response.Headers.Location!.OriginalString);
        Assert.Equal(CatalogSeedIds.Copy(12, 1), Assert.Single(await LoansAsync(Members.AshotBaghdasaryan)).BookCopyId);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(ReservationStatus.Fulfilled, (await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Reservations
                .AsNoTracking().SingleAsync(r => r.Id == reservationId)).Status);

        string next = await browser.FollowAsync(response);
        Assert.Contains("<strong>Issued</strong>", next);
        Assert.DoesNotContain($"<td data-label=\"Book\">{await TitleAsync(12)}</td>", next);
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_is_refused()
    {
        var response = await (await LibrarianAsync()).PostRawAsync($"{Desk}?handler=Issue",
            ("Member", EmployeeId(Members.VahanStepanyan)), ("Barcode", CatalogSeedIds.CopyBarcode(13, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await LoansAsync(Members.VahanStepanyan));
    }
}
