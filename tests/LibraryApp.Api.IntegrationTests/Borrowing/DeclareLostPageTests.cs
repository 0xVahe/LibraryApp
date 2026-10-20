using System.Net;
using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Application.Abstractions.Catalog;
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
/// G3-22: "Declare lost" (BOR-09) by the UI guide (Part 1 <c>DeclareLostButton</c>, demo <c>desk.html</c>,
/// <c>staff-member.html</c>) — next to "Take back" on the Loan desk (by copy barcode) and per open loan in
/// <c>StaffBorrowingSummary</c>. The loan and the copy become Lost and the lost points are added (G3-07). Library staff only.
/// Every test uses its own members and books.
/// </summary>
public class DeclareLostPageTests(BorrowingUiHost factory) : IClassFixture<BorrowingUiHost>
{
    private const string Desk = "/desk";

    private static string FullName(Guid member) => StaffSeedData.People.Single(p => p.StaffMemberId == member) is var p ? $"{p.FirstName} {p.LastName}" : "";

    private Task<UiBrowser> LibrarianAsync() => UiBrowser.SignInAsync(factory, Members.AramSargsyan);

    private async Task<LoanDto> IssueAsync(Guid member, int book)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILoanService>()
            .IssueAsync(new IssueLoanRequest(member, CatalogSeedIds.CopyBarcode(book, 1)));
    }

    private async Task<(LoanStatus Loan, BookCopyStatus Copy, int Points)> StateAsync(Guid loanId, int book)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Loan loan = await db.Loans.AsNoTracking().SingleAsync(l => l.Id == loanId);
        BookCopy copy = await db.BookCopies.AsNoTracking().SingleAsync(c => c.Id == CatalogSeedIds.Copy(book, 1));
        int points = await db.PenaltyRecords.AsNoTracking().Where(p => p.LoanId == loanId).SumAsync(p => p.Points);
        return (loan.Status, copy.Status, points);
    }

    private async Task<string> TitleAsync(int book)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ICatalogService>().GetBookAsync(CatalogSeedIds.Book(book)))!.Title;
    }

    [Fact]
    public async Task The_desk_offers_declare_lost_next_to_take_back()
    {
        var (_, html, _) = await (await LibrarianAsync()).GetAsync(Desk);

        Assert.Contains("<button class=\"btn btn-danger\" type=\"submit\" formaction=\"/desk?handler=Lost\">Declare lost</button>", html);
        Assert.Contains("Declare lost adds the lost points and marks the copy Lost.", html);
    }

    [Fact]
    public async Task Declare_lost_at_the_desk_marks_the_loan_and_the_copy_lost_with_the_points_BOR_09()
    {
        LoanDto loan = await IssueAsync(Members.RubenHarutyunyan, 4);
        var browser = await LibrarianAsync();

        var response = await browser.PostAsync(Desk, $"{Desk}?handler=Lost", ("ReturnBarcode", $" {CatalogSeedIds.CopyBarcode(4, 1).ToLowerInvariant()} "));

        Assert.Equal(Desk, response.Headers.Location!.OriginalString);
        var (loanStatus, copyStatus, points) = await StateAsync(loan.LoanId, 4);
        Assert.Equal((LoanStatus.Lost, BookCopyStatus.Lost), (loanStatus, copyStatus));
        Assert.True(points > 0);

        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-amber", next);
        Assert.Contains("<strong>Declared lost</strong>", next);
        Assert.Contains($"{Texts.Quoted(await TitleAsync(4))} ({CatalogSeedIds.CopyBarcode(4, 1)}) from {FullName(Members.RubenHarutyunyan)}.", next);
        Assert.Contains($"{Texts.Count(points, "penalty point")} added. The copy is marked Lost and leaves circulation.", next);
    }

    [Fact]
    public async Task A_copy_that_is_not_on_loan_cannot_be_declared_lost_at_the_desk()
    {
        var response = await (await LibrarianAsync()).PostAsync(Desk, $"{Desk}?handler=Lost", ("ReturnBarcode", CatalogSeedIds.CopyBarcode(5, 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await UiBrowser.HtmlAsync(response);
        Assert.Contains("<strong>The book was not taken back.</strong>", html);
        Assert.Contains(BorrowingErrors.NoOpenLoanForCopy.Message, html);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(BookCopyStatus.Available, (await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().BookCopies
            .AsNoTracking().SingleAsync(c => c.Id == CatalogSeedIds.Copy(5, 1))).Status);
    }

    [Fact]
    public async Task An_empty_barcode_is_a_field_error()
    {
        var response = await (await LibrarianAsync()).PostAsync(Desk, $"{Desk}?handler=Lost", ("ReturnBarcode", " "));

        string html = await UiBrowser.HtmlAsync(response);
        Assert.Contains("<span class=\"error-text\">Enter the copy barcode.</span>", html);
        Assert.Matches("<input id=\"return-barcode\"[^>]*aria-invalid=\"true\"", html);
    }

    [Fact]
    public async Task Each_open_loan_on_the_staff_member_page_has_a_declare_lost_button_and_it_comes_back_there()
    {
        LoanDto loan = await IssueAsync(Members.AnahitMartirosyan, 6);
        var browser = await LibrarianAsync();
        string page = $"/staff/{Members.AnahitMartirosyan}";

        var (_, html, _) = await browser.GetAsync(page);
        Assert.Contains("action=\"/loans/lost\"", html);
        Assert.Contains($"name=\"LoanId\" value=\"{loan.LoanId}\"", html);
        Assert.Contains($"aria-label=\"Declare {await TitleAsync(6)} lost\"", html);

        var response = await browser.PostAsync(page, "/loans/lost", ("LoanId", loan.LoanId.ToString()), ("ReturnUrl", page));

        Assert.Equal(page, response.Headers.Location!.OriginalString);
        var (loanStatus, copyStatus, points) = await StateAsync(loan.LoanId, 6);
        Assert.Equal((LoanStatus.Lost, BookCopyStatus.Lost), (loanStatus, copyStatus));
        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-amber", next);
        Assert.Contains($"{Texts.Count(points, "penalty point")} added.", next);
        Assert.DoesNotContain($"name=\"LoanId\" value=\"{loan.LoanId}\"", next);   // no longer an open loan
        Assert.Contains($"<span class=\"stat-value\">{points}</span>", next);
    }

    [Fact]
    public async Task A_loan_that_is_already_closed_gets_a_red_alert()
    {
        LoanDto loan = await IssueAsync(Members.ElenDanielyan, 7);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ILoanReturnService>().ReturnAsync(loan.LoanId, new ReturnLoanRequest(false));
        var browser = await LibrarianAsync();
        string page = $"/staff/{Members.ElenDanielyan}";

        var response = await browser.PostAsync(page, "/loans/lost", ("LoanId", loan.LoanId.ToString()), ("ReturnUrl", page));

        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-red", next);
        Assert.Contains("The copy was not declared lost.", next);
        Assert.Contains(BorrowingErrors.LoanAlreadyClosed.Message, next);
        Assert.Equal(LoanStatus.Returned, (await StateAsync(loan.LoanId, 7)).Loan);
    }

    [Fact]
    public async Task An_unknown_loan_is_not_found_and_an_outside_return_url_falls_back_to_the_staff_list()
    {
        var browser = await LibrarianAsync();

        var unknown = await browser.PostAsync(Desk, "/loans/lost", ("LoanId", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        LoanDto loan = await IssueAsync(Members.SamvelPoghosyan, 8);
        var response = await browser.PostAsync(Desk, "/loans/lost", ("LoanId", loan.LoanId.ToString()), ("ReturnUrl", "https://example.com/"));
        Assert.Equal("/staff", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_staff_member_cannot_declare_a_loan_lost()
    {
        LoanDto loan = await IssueAsync(Members.AniSahakyan, 9);
        var browser = await UiBrowser.SignInAsync(factory, Members.AniSahakyan);

        var response = await browser.PostAsync("/forbidden", "/loans/lost", ("LoanId", loan.LoanId.ToString()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
        Assert.Equal(LoanStatus.Active, (await StateAsync(loan.LoanId, 9)).Loan);

        var desk = await browser.PostAsync("/forbidden", $"{Desk}?handler=Lost", ("ReturnBarcode", CatalogSeedIds.CopyBarcode(9, 1)));
        Assert.Equal("/forbidden", desk.Headers.Location!.AbsolutePath);
        Assert.Equal(LoanStatus.Active, (await StateAsync(loan.LoanId, 9)).Loan);
    }

    [Fact]
    public async Task The_lost_page_takes_only_a_post_with_the_antiforgery_token()
    {
        LoanDto loan = await IssueAsync(Members.LianaNalbandyan, 10);
        var browser = await LibrarianAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/loans/lost")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostRawAsync("/loans/lost", ("LoanId", loan.LoanId.ToString()))).StatusCode);
        Assert.Equal(LoanStatus.Active, (await StateAsync(loan.LoanId, 10)).Loan);
    }
}
