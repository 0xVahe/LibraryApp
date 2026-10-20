using System.Net;
using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// G3-19: the Borrowing blocks of other groups' pages by the UI guide (Part 1 "Group 3", demo <c>my-library.html</c>,
/// <c>staff-member.html</c>): <c>OverdueBanner</c>, <c>MyLoans</c> with Renew (BOR-04), <c>MyReservations</c> with Cancel
/// (BOR-12), <c>MyPenalties</c> (BR-BOR-6) and <c>StaffBorrowingSummary</c> on Group 2's Staff member page. Own data only
/// (NFR-03). Every test uses its own members and books.
/// </summary>
public class BorrowingBlocksPageTests(BorrowingUiHost factory) : IClassFixture<BorrowingUiHost>
{
    private static string Block(string component, Guid member) => $"/ui-test/borrowing/blocks/{component}/{member}";

    private async Task<string> TitleAsync(int book)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ICatalogService>().GetBookAsync(CatalogSeedIds.Book(book)))!.Title;
    }

    private async Task<LoanDto> IssueAsync(Guid member, int book, int copy = 1)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILoanService>()
            .IssueAsync(new IssueLoanRequest(member, CatalogSeedIds.CopyBarcode(book, copy)));
    }

    /// <summary>A loan issued 30 days ago for 14 days and marked Overdue by the daily job; its copy is on loan.</summary>
    private async Task<Loan> OverdueLoanAsync(Guid member, int book)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var loan = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(book, 1), member, clock.UtcNow.AddDays(-30), 14);
        loan.MarkOverdue(clock.UtcNow);
        db.Loans.Add(loan);
        (await db.BookCopies.SingleAsync(c => c.Id == CatalogSeedIds.Copy(book, 1))).ChangeStatus(BookCopyStatus.OnLoan);
        await db.SaveChangesAsync();
        return loan;
    }

    private async Task<LendingRules> RulesAsync(Guid member)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffMemberService>().GetLendingRulesAsync(member);
    }

    private Task<DateOnly> LocalDateAsync(DateTime utc) =>
        Task.FromResult(factory.Services.GetRequiredService<IClock>().ToLocalDate(utc));

    [Fact]
    public async Task My_loans_lists_each_open_loan_with_its_copy_due_date_badge_and_renew_button()
    {
        LoanDto loan = await IssueAsync(Members.ManeGevorgyan, 20);
        var rules = await RulesAsync(Members.ManeGevorgyan);
        var browser = await UiBrowser.SignInAsync(factory, Members.ManeGevorgyan);

        var (status, html, _) = await browser.GetAsync(Block("MyLoans", Members.ManeGevorgyan));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<section class=\"card\" aria-labelledby=\"my-loans-title\">", html);
        Assert.Contains($"<a class=\"title\" href=\"/catalog/books/{CatalogSeedIds.Book(20)}\">{await TitleAsync(20)}</a>", html);
        Assert.Contains($"Copy {CatalogSeedIds.CopyBarcode(20, 1)}", html);
        Assert.Contains($"Due {Texts.Date(await LocalDateAsync(loan.DueAtUtc))} · renewed 0 of {rules.MaxRenewals} times", html);
        Assert.Contains("<span class=\"badge badge-blue\">On loan</span>", html);   // 30 days left: not due soon
        Assert.Contains("action=\"/loans/renew\"", html);
        Assert.Contains($"name=\"LoanId\" value=\"{loan.LoanId}\"", html);
        Assert.Contains("class=\"cover", html);
        Assert.DoesNotContain("style=\"", html);
    }

    [Fact]
    public async Task Renew_moves_the_due_date_and_comes_back_with_a_green_alert_BOR_04()
    {
        LoanDto loan = await IssueAsync(Members.NarekSimonyan, 21);
        var browser = await UiBrowser.SignInAsync(factory, Members.NarekSimonyan);
        string page = Block("MyLoans", Members.NarekSimonyan);

        var response = await browser.PostAsync(page, "/loans/renew", ("LoanId", loan.LoanId.ToString()), ("ReturnUrl", page));

        Assert.Equal(page, response.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        LoanDto renewed = await scope.ServiceProvider.GetRequiredService<ILoanService>().GetAsync(loan.LoanId);
        Assert.Equal(1, renewed.RenewalCount);
        string alert = await browser.AlertAfterAsync(response);
        Assert.Contains("alert alert-green", alert);
        Assert.Contains($"The new due date is {Texts.Date(await LocalDateAsync(renewed.DueAtUtc))}.", alert);
        Assert.Contains("renewed 1 of 2 times", (await browser.GetAsync(page)).Html);   // Manager / Senior Specialist: 2 renewals
    }

    [Fact]
    public async Task A_refused_renewal_comes_back_with_the_reason_in_a_red_alert()
    {
        // Restricted: one loan, no renewals (BR-MEM-3) — no button, and a direct post is refused by the service.
        LoanDto loan = await IssueAsync(Members.GorMkrtchyan, 22);
        var browser = await UiBrowser.SignInAsync(factory, Members.GorMkrtchyan);
        string page = Block("MyLoans", Members.GorMkrtchyan);

        var (_, html, _) = await browser.GetAsync(page);
        Assert.DoesNotContain("action=\"/loans/renew\"", html);
        Assert.Contains("no renewals", html);

        var response = await browser.PostAsync("/forbidden", "/loans/renew", ("LoanId", loan.LoanId.ToString()), ("ReturnUrl", page));
        Assert.Equal(page, response.Headers.Location!.OriginalString);
        string next = await browser.AlertAfterAsync(response);
        Assert.Contains("alert alert-red", next);
        Assert.Contains("The loan was not renewed.", next);
        Assert.Contains(BorrowingErrors.RestrictedRenewalForbidden.Message, next);
    }

    [Fact]
    public async Task Renewing_the_loan_of_someone_else_is_not_found_NFR_03()
    {
        LoanDto loan = await IssueAsync(Members.TigranMelikyan, 23);
        var browser = await UiBrowser.SignInAsync(factory, Members.EvaHovsepyan);

        var response = await browser.PostAsync("/forbidden", "/loans/renew", ("LoanId", loan.LoanId.ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<ILoanService>().GetAsync(loan.LoanId)).RenewalCount);
    }

    [Theory]
    [InlineData("https://example.com/phish", 24)]
    [InlineData("//example.com", 25)]
    [InlineData("", 26)]
    public async Task The_way_back_is_a_local_url_or_my_library(string returnUrl, int book)
    {
        LoanDto loan = await IssueAsync(Members.ArmenBabayan, book);
        var browser = await UiBrowser.SignInAsync(factory, Members.ArmenBabayan);

        var response = await browser.PostAsync("/forbidden", "/loans/renew", ("LoanId", loan.LoanId.ToString()), ("ReturnUrl", returnUrl));

        Assert.Equal("/my-library", response.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ILoanReturnService>().ReturnAsync(loan.LoanId, new ReturnLoanRequest(false));
    }

    [Fact]
    public async Task The_renew_and_cancel_pages_take_only_a_post_with_the_antiforgery_token()
    {
        LoanDto loan = await IssueAsync(Members.LevonKhachatryan, 27);
        var browser = await UiBrowser.SignInAsync(factory, Members.LevonKhachatryan);

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/loans/renew")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/reservations/cancel")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostRawAsync("/loans/renew", ("LoanId", loan.LoanId.ToString()))).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<ILoanService>().GetAsync(loan.LoanId)).RenewalCount);
    }

    [Fact]
    public async Task An_overdue_loan_has_a_red_badge_in_working_days_no_renew_button_and_the_banner()
    {
        await OverdueLoanAsync(Members.MilenaAbrahamyan, 28);
        var browser = await UiBrowser.SignInAsync(factory, Members.MilenaAbrahamyan);

        var (_, loans, _) = await browser.GetAsync(Block("MyLoans", Members.MilenaAbrahamyan));
        Assert.Matches("<span class=\"badge badge-red\">Overdue · \\d+ working days?</span>", loans);
        Assert.DoesNotContain("action=\"/loans/renew\"", loans);

        var (_, banner, _) = await browser.GetAsync(Block("OverdueBanner", Members.MilenaAbrahamyan));
        Assert.Contains("<div class=\"alert alert-red\" role=\"alert\">", banner);
        Assert.Contains("<strong>1 book is overdue</strong>", banner);
        Assert.Contains($"Return {Texts.Quoted(await TitleAsync(28))} to the library desk.", banner);
        Assert.Contains("Every overdue working day adds penalty points.", banner);
        Assert.DoesNotContain("floor", banner);
    }

    [Fact]
    public async Task The_banner_is_empty_without_overdue_loans()
    {
        await IssueAsync(Members.ArturMinasyan, 29);
        var browser = await UiBrowser.SignInAsync(factory, Members.ArturMinasyan);

        var (status, html, _) = await browser.GetAsync(Block("OverdueBanner", Members.ArturMinasyan));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("", html.Trim());
    }

    [Fact]
    public async Task A_waiting_reservation_shows_the_queue_place_and_is_cancelled_BOR_12()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var loans = scope.ServiceProvider.GetRequiredService<ILoanService>();
            Guid[] borrowers = [Members.KarenAsatryan, Members.SofiaHambardzumyan, Members.AnahitMartirosyan, Members.ElenDanielyan];
            for (int copy = 1; copy <= CatalogSeedData.CopiesPerBook[30]; copy++)
                await loans.IssueAsync(new IssueLoanRequest(borrowers[copy - 1], CatalogSeedIds.CopyBarcode(30, copy)));
        }
        var browser = await UiBrowser.SignInAsync(factory, Members.NuneTorosyan);
        await browser.PostAsync("/forbidden", "/reservations", ("BookId", CatalogSeedIds.Book(30).ToString()));
        string page = Block("MyReservations", Members.NuneTorosyan);

        var (_, html, _) = await browser.GetAsync(page);
        Assert.Contains("<section class=\"card\" aria-labelledby=\"my-reservations-title\">", html);
        Assert.Contains("you are 1st in the queue", html);
        Assert.Contains("<span class=\"badge badge-grey\">Waiting</span>", html);
        Assert.Contains("action=\"/reservations/cancel\"", html);

        Reservation reservation;
        using (var scope = factory.Services.CreateScope())
            reservation = await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Reservations.AsNoTracking()
                .SingleAsync(r => r.StaffMemberId == Members.NuneTorosyan);
        var response = await browser.PostAsync(page, "/reservations/cancel", ("ReservationId", reservation.Id.ToString()), ("ReturnUrl", page));

        string alert = await browser.AlertAfterAsync(response);
        Assert.Contains("alert alert-green", alert);
        Assert.Contains("Reservation cancelled.", alert);
        Assert.Contains("No reservations.", (await browser.GetAsync(page)).Html);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(ReservationStatus.Cancelled, (await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Reservations
                .AsNoTracking().SingleAsync(r => r.Id == reservation.Id)).Status);
    }

    [Fact]
    public async Task A_copy_held_for_the_member_shows_the_pickup_deadline()
    {
        DateOnly until;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var reservation = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(31), Members.AlinaMirzoyan, clock.UtcNow.AddDays(-3), 1);
            DateTime readyUntil = clock.UtcNow.AddDays(2);
            reservation.MarkReady(CatalogSeedIds.Copy(31, 1), readyUntil);
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync();
            until = clock.ToLocalDate(readyUntil);
        }
        var browser = await UiBrowser.SignInAsync(factory, Members.AlinaMirzoyan);

        var (_, html, _) = await browser.GetAsync(Block("MyReservations", Members.AlinaMirzoyan));

        Assert.Contains($"Pick up before {Texts.Date(until)} at the library desk", html);
        Assert.Contains("<span class=\"badge badge-green\">Ready for pickup</span>", html);
    }

    [Fact]
    public async Task My_penalties_lists_the_active_records_with_their_points_and_expiry()
    {
        Loan loan;
        DateOnly expires;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            loan = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(32, 1), Members.ErikZakaryan, clock.UtcNow.AddDays(-40), 14);
            loan.Return(clock.UtcNow.AddDays(-10));
            var penalty = PenaltyRecord.Create(Guid.NewGuid(), Members.ErikZakaryan, loan.Id, 3, "Late return: 3 overdue working day(s).",
                clock.UtcNow.AddDays(-10), clock.UtcNow.AddDays(80));
            db.Loans.Add(loan);
            db.PenaltyRecords.Add(penalty);
            await db.SaveChangesAsync();
            expires = clock.ToLocalDate(penalty.ExpiresAtUtc);
        }
        var browser = await UiBrowser.SignInAsync(factory, Members.ErikZakaryan);

        var (_, html, _) = await browser.GetAsync(Block("MyPenalties", Members.ErikZakaryan));

        Assert.Contains("<section class=\"card\" aria-labelledby=\"my-penalties-title\">", html);
        Assert.Contains("<p class=\"title\">Late return: 3 overdue working day(s).</p>", html);
        Assert.Contains($"{await TitleAsync(32)} · until {Texts.Date(expires)}", html);
        Assert.Contains("<span class=\"badge badge-amber\">3 points</span>", html);
        Assert.Contains("3 active points in total.", html);
    }

    [Theory]
    [InlineData("MyLoans", 33)]
    [InlineData("MyReservations", 34)]
    [InlineData("MyPenalties", 35)]
    [InlineData("OverdueBanner", 36)]
    [InlineData("StaffBorrowingSummary", 37)]
    public async Task A_staff_member_never_sees_the_blocks_of_someone_else_NFR_03(string component, int book)
    {
        await OverdueLoanAsync(Members.LusineKarapetyan, book);
        var browser = await UiBrowser.SignInAsync(factory, Members.DavitHakobyan);

        var (status, html, _) = await browser.GetAsync(Block(component, Members.LusineKarapetyan));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("", html.Trim());
    }

    [Fact]
    public async Task The_staff_member_page_of_Group_2_shows_the_loans_reservations_and_standing()
    {
        LoanDto loan = await IssueAsync(Members.SamvelPoghosyan, 40);
        var browser = await UiBrowser.SignInAsync(factory, Members.AramSargsyan);

        var (status, html, _) = await browser.GetAsync($"/staff/{Members.SamvelPoghosyan}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("Borrowing details are currently unavailable.", html);
        Assert.Contains("<section class=\"card flush\" aria-labelledby=\"staff-loans-title\">", html);
        Assert.Contains($"<td data-label=\"Book\">{await TitleAsync(40)}</td>", html);
        Assert.Contains($"<td class=\"nowrap\" data-label=\"Copy\">{CatalogSeedIds.CopyBarcode(40, 1)}</td>", html);
        Assert.Contains($"<td data-label=\"Due\">{Texts.Date(await LocalDateAsync(loan.DueAtUtc))}</td>", html);
        Assert.Contains("<span class=\"badge badge-blue\">On loan</span>", html);
        Assert.Contains("<section class=\"card\" aria-labelledby=\"staff-standing-title\">", html);
        Assert.Contains("<span class=\"stat-value\">0</span>", html);
    }

    [Theory]
    [InlineData(-3, "badge-red", "Overdue")]
    [InlineData(0, "badge-amber", "Due today")]
    [InlineData(1, "badge-amber", "Due tomorrow")]
    [InlineData(5, "badge-amber", "Due in 5 days")]
    [InlineData(6, "badge-blue", "On loan")]
    public void The_loan_badge_follows_the_due_date(int daysLeft, string colour, string text)
    {
        var book = new BorrowedBook(Guid.Empty, "Book", null, new("Book", null, null, Guid.Empty, Mini: true), null);
        var line = new LoanLine(Guid.Empty, book, "BC-1", DateOnly.MinValue, daysLeft < 0, 0, daysLeft, 0);

        Assert.Equal(($"badge {colour}", text), (line.Badge.CssClass, line.Badge.Text));
    }

    [Fact]
    public void A_past_due_date_is_never_due_today_even_before_the_loan_is_marked_overdue()
    {
        var book = new BorrowedBook(Guid.Empty, "Book", null, new("Book", null, null, Guid.Empty, Mini: true), null);

        var line = new LoanLine(Guid.Empty, book, null, DateOnly.MinValue, IsOverdue: false, 0, DaysLeft: -1, 0);

        Assert.Equal(("badge badge-red", "Overdue"), (line.Badge.CssClass, line.Badge.Text));
    }

    [Fact]
    public async Task A_loan_due_yesterday_that_the_daily_job_has_not_marked_yet_is_overdue_on_the_page()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            // Issued 15 days ago for 14 days: due yesterday, still Active (the job runs once a day).
            db.Loans.Add(Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(41, 1), Members.AnahitMartirosyan, clock.UtcNow.AddDays(-15), 14));
            (await db.BookCopies.SingleAsync(c => c.Id == CatalogSeedIds.Copy(41, 1))).ChangeStatus(BookCopyStatus.OnLoan);
            await db.SaveChangesAsync();
        }
        var browser = await UiBrowser.SignInAsync(factory, Members.AnahitMartirosyan);

        var (_, html, _) = await browser.GetAsync(Block("MyLoans", Members.AnahitMartirosyan));

        Assert.Matches("<span class=\"badge badge-red\">Overdue( · \\d+ working days?)?</span>", html);
        Assert.DoesNotContain("Due today", html);
        Assert.DoesNotContain("action=\"/loans/renew\"", html);
    }

    [Fact]
    public void Overdue_days_are_working_days()
    {
        var book = new BorrowedBook(Guid.Empty, "Book", null, new("Book", null, null, Guid.Empty, Mini: true), null);

        Assert.Equal("Overdue · 1 working day", new LoanLine(Guid.Empty, book, null, DateOnly.MinValue, true, 1, -1, 0).Badge.Text);
        Assert.Equal("Overdue · 6 working days", new LoanLine(Guid.Empty, book, null, DateOnly.MinValue, true, 6, -8, 0).Badge.Text);
    }
}
