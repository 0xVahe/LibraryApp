using System.Net;
using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// G3-24: the Loan history page <c>/staff/{id}/loans</c> by the UI guide (Part 2 "Loan history", demo <c>staff-loans.html</c>):
/// every loan of the member, also Returned and Lost (BOR-10), filters status and issued from/to, the points of each loan,
/// the shared pager; library staff only. Every test uses its own members.
/// </summary>
public class LoanHistoryPageTests(BorrowingUiHost factory) : IClassFixture<BorrowingUiHost>
{
    private static string History(Guid member, string query = "") => $"/staff/{member}/loans{query}";

    private Task<UiBrowser> LibrarianAsync() => UiBrowser.SignInAsync(factory, Members.AramSargsyan);

    private IClock Clock => factory.Services.GetRequiredService<IClock>();

    private async Task<string> TitleAsync(int book)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ICatalogService>().GetBookAsync(CatalogSeedIds.Book(book)))!.Title;
    }

    /// <summary>
    /// Four loans of the member: returned late 50 days ago with 2 points (and a waived point that no longer counts), lost
    /// 30 days ago with 10 points, overdue (a copy of <paramref name="openBook"/>), and on loan (the next book, newest). The open
    /// loans need copies of their own: one open loan per copy.
    /// </summary>
    private async Task<(Loan Returned, Loan Lost, Loan Overdue, Loan Active)> HistoryAsync(Guid member, int openBook)
    {
        DateTime now = Clock.UtcNow;
        var returned = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(41, 1), member, now.AddDays(-70), 14);
        returned.Return(now.AddDays(-50));
        var lost = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(42, 1), member, now.AddDays(-45), 14);
        lost.MarkLost(now.AddDays(-30));
        var overdue = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(openBook, 1), member, now.AddDays(-25), 14);
        overdue.MarkOverdue(now);
        var active = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(openBook + 1, 1), member, now.AddDays(-2), 14);

        var late = PenaltyRecord.Create(Guid.NewGuid(), member, returned.Id, 2, "Late return: 2 overdue working day(s).", now.AddDays(-50), now.AddDays(40));
        var waived = PenaltyRecord.Create(Guid.NewGuid(), member, returned.Id, 1, "Copy returned damaged.", now.AddDays(-50), now.AddDays(40));
        waived.Waive("Was already damaged");
        var lostPoints = PenaltyRecord.Create(Guid.NewGuid(), member, lost.Id, 10, "Copy declared lost.", now.AddDays(-30), now.AddDays(60));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        db.Loans.AddRange(returned, lost, overdue, active);
        db.PenaltyRecords.AddRange(late, waived, lostPoints);
        await db.SaveChangesAsync();
        return (returned, lost, overdue, active);
    }

    private string Date(DateTime utc) => Texts.Date(Clock.ToLocalDate(utc));

    private string Iso(DateTime utc) => Clock.ToLocalDate(utc).ToString("yyyy-MM-dd");

    private static string RowOf(string html, string title)
    {
        int start = html.IndexOf($"<td data-label=\"Book\">{title}</td>", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No row for {title}.");
        return html[start..html.IndexOf("</tr>", start, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task Every_loan_of_the_member_is_listed_newest_first_with_its_dates_status_and_points_BOR_10()
    {
        var (returned, lost, overdue, active) = await HistoryAsync(Members.RubenHarutyunyan, 1);

        var (status, html, _) = await (await LibrarianAsync()).GetAsync(History(Members.RubenHarutyunyan));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<h1>Loan history</h1>", html);
        Assert.Contains("Ruben Harutyunyan — every loan, also returned and lost ones.", html);
        Assert.Matches($@"<p class=""crumbs"">\s*<a href=""/staff"">Staff members</a> /\s*<a href=""/staff/{Members.RubenHarutyunyan}"">Ruben Harutyunyan</a> /\s*Loan history", html);
        Assert.Contains("<p class=\"small muted\" role=\"status\">4 loans</p>", html);
        Assert.Contains("<th scope=\"col\">Returned</th><th scope=\"col\">Status</th><th scope=\"col\">Points</th>", html);

        string[] titles = [await TitleAsync(2), await TitleAsync(1), await TitleAsync(42), await TitleAsync(41)];
        int[] order = titles.Select(t => html.IndexOf($"<td data-label=\"Book\">{t}</td>", StringComparison.Ordinal)).ToArray();
        Assert.Equal(order.OrderBy(i => i), order);   // newest issue first

        string returnedRow = RowOf(html, titles[3]);
        Assert.Contains($"<td class=\"nowrap\" data-label=\"Copy\">{CatalogSeedIds.CopyBarcode(41, 1)}</td>", returnedRow);
        Assert.Contains($"<td data-label=\"Issued\">{Date(returned.IssuedAtUtc)}</td>", returnedRow);
        Assert.Contains($"<td data-label=\"Due\">{Date(returned.DueAtUtc)}</td>", returnedRow);
        Assert.Contains($"<td data-label=\"Returned\">{Date(returned.ReturnedAtUtc!.Value)}</td>", returnedRow);
        Assert.Contains("<span class=\"badge badge-green\">Returned</span>", returnedRow);
        Assert.Contains("<td data-label=\"Points\">2</td>", returnedRow);       // the waived point is not counted

        string lostRow = RowOf(html, titles[2]);
        Assert.Contains("<span class=\"badge badge-red\">Lost</span>", lostRow);
        Assert.Contains("<td data-label=\"Returned\">—</td>", lostRow);
        Assert.Contains("<td data-label=\"Points\">10</td>", lostRow);

        Assert.Contains("<span class=\"badge badge-red\">Overdue</span>", RowOf(html, titles[1]));
        string activeRow = RowOf(html, titles[0]);
        Assert.Contains("<span class=\"badge badge-blue\">On loan</span>", activeRow);
        Assert.Contains("<td data-label=\"Points\">—</td>", activeRow);
        Assert.Contains("<a href=\"/staff\" aria-current=\"page\">Staff members</a>", html);
        Assert.DoesNotContain("style=\"", html);
        Assert.DoesNotContain("class=\"pager\"", html);
    }

    [Fact]
    public async Task The_status_filter_keeps_one_status_and_stays_selected()
    {
        var (returned, _, _, _) = await HistoryAsync(Members.AnahitMartirosyan, 3);

        var (_, html, _) = await (await LibrarianAsync()).GetAsync(History(Members.AnahitMartirosyan, "?status=Returned"));

        Assert.Contains("1 loan</p>", html);
        Assert.Contains(await TitleAsync(41), html);
        Assert.DoesNotContain($"<td data-label=\"Book\">{await TitleAsync(42)}</td>", html);
        Assert.Contains("<option value=\"Returned\" selected=\"selected\">Returned</option>", html);
        Assert.Contains("<option value=\"Active\">On loan</option>", html);
    }

    [Fact]
    public async Task The_issue_period_filter_keeps_the_loans_issued_in_it_and_says_the_period()
    {
        var (_, lost, overdue, _) = await HistoryAsync(Members.ElenDanielyan, 5);
        string query = $"?from={Iso(lost.IssuedAtUtc)}&to={Iso(overdue.IssuedAtUtc)}";

        var (_, html, _) = await (await LibrarianAsync()).GetAsync(History(Members.ElenDanielyan, query));

        Assert.Contains($"2 loans · {Date(lost.IssuedAtUtc)} – {Date(overdue.IssuedAtUtc)}</p>", html);
        Assert.Contains($"<td data-label=\"Book\">{await TitleAsync(42)}</td>", html);
        Assert.Contains($"<td data-label=\"Book\">{await TitleAsync(5)}</td>", html);
        Assert.DoesNotContain($"<td data-label=\"Book\">{await TitleAsync(41)}</td>", html);
        Assert.Contains($"value=\"{Iso(lost.IssuedAtUtc)}\"", html);
    }

    [Fact]
    public async Task A_period_that_ends_before_it_starts_is_a_field_error()
    {
        await HistoryAsync(Members.SamvelPoghosyan, 7);

        var (status, html, _) = await (await LibrarianAsync()).GetAsync(History(Members.SamvelPoghosyan, "?from=2026-10-10&to=2026-10-01"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Matches("<input id=\"history-to\"[^>]*aria-invalid=\"true\"", html);
        Assert.Contains($"<span class=\"error-text\">{BorrowingErrors.HistoryPeriodInvalid.Message}</span>", html);
        Assert.Contains("No loans match these filters.", html);
    }

    [Fact]
    public async Task No_match_offers_the_whole_history_and_an_unknown_status_shows_every_loan()
    {
        await HistoryAsync(Members.AniSahakyan, 9);
        var browser = await LibrarianAsync();

        var (_, none, _) = await browser.GetAsync(History(Members.AniSahakyan, "?from=2020-01-01&to=2020-01-31"));
        Assert.Contains("No loans match these filters.", none);
        Assert.Contains($"<a class=\"link-btn\" href=\"/staff/{Members.AniSahakyan}/loans\">Show every loan</a>", none);

        var (status, all, _) = await browser.GetAsync(History(Members.AniSahakyan, "?status=Lent"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("4 loans</p>", all);
    }

    [Fact]
    public async Task A_member_without_loans_has_an_empty_history()
    {
        var (_, html, _) = await (await LibrarianAsync()).GetAsync(History(Members.VahanStepanyan));

        Assert.Contains("0 loans</p>", html);
        Assert.Contains("No loans yet.", html);
        Assert.DoesNotContain("Show every loan", html);
    }

    [Fact]
    public async Task More_than_one_page_has_the_shared_pager_and_keeps_the_filters()
    {
        DateTime now = Clock.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            for (int i = 0; i < 21; i++)
            {
                var loan = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(45, 1), Members.LianaNalbandyan, now.AddDays(-100 + i), 14);
                loan.Return(now.AddDays(-99 + i));
                db.Loans.Add(loan);
            }
            await db.SaveChangesAsync();
        }
        var browser = await LibrarianAsync();

        var (_, first, _) = await browser.GetAsync(History(Members.LianaNalbandyan, "?status=Returned"));
        Assert.Contains("21 loans</p>", first);
        Assert.Contains("<nav class=\"pager\" aria-label=\"Pages\">", first);
        Assert.Contains($"href=\"/staff/{Members.LianaNalbandyan}/loans?status=Returned&page=2\"", first);

        var (_, second, _) = await browser.GetAsync(History(Members.LianaNalbandyan, "?status=Returned&page=2"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(second, "<td data-label=\"Book\">"));
    }

    [Fact]
    public async Task An_unknown_staff_member_is_the_not_found_page()
    {
        var (status, _, _) = await (await LibrarianAsync()).GetAsync(History(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task A_staff_member_is_sent_to_the_forbidden_page_even_for_their_own_history()
    {
        var browser = await UiBrowser.SignInAsync(factory, Members.SofiaHambardzumyan);

        var (status, _, response) = await browser.GetAsync(History(Members.SofiaHambardzumyan));

        Assert.Equal(HttpStatusCode.Redirect, status);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task The_staff_member_page_links_to_the_loan_history()
    {
        var (_, html, _) = await (await LibrarianAsync()).GetAsync($"/staff/{Members.KarenAsatryan}");

        Assert.Contains($"<a class=\"btn btn-secondary btn-sm\" href=\"/staff/{Members.KarenAsatryan}/loans\">Loan history</a>", html);
    }
}
