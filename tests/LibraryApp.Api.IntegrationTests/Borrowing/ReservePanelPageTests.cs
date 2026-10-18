using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The host for the "Get this book" panel: the Catalog and Staff seed without the Borrowing seed (the tests own every
/// loan and reservation), plus a test controller that renders the component the way Group 1's Book page will.
/// </summary>
public sealed class ReservePanelHost : ApiHostFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.Remove(services.Single(d => d.ServiceType == typeof(IModuleSeeder) && d.ImplementationType == typeof(BorrowingSeeder)));
        services.AddControllers().AddApplicationPart(typeof(ReservePanelTestController).Assembly);
    }
}

[Route("ui-test/borrowing")]
[Authorize]
public sealed class ReservePanelTestController : Controller
{
    /// <summary>What the Book page does: <c>@await Component.InvokeAsync("ReservePanel", new { bookId })</c>.</summary>
    [HttpGet("reserve-panel/{bookId:guid}")]
    public IActionResult Panel(Guid bookId) => ViewComponent("ReservePanel", new { bookId });
}

/// <summary>
/// G3-25: <c>ReservePanel</c> by the UI guide (Part 1 "Group 3", demo <c>book.html</c>) and its form, through the UI
/// cookie of seeded members (BOR-07, BR-BOR-3). Every test uses its own members and books.
/// </summary>
public class ReservePanelPageTests(ReservePanelHost factory) : IClassFixture<ReservePanelHost>
{
    private const string ReserveButton = "Reserve a copy";

    private static string Email(Guid staffMemberId) => StaffSeedData.People.Single(p => p.StaffMemberId == staffMemberId).Email;

    private static string Panel(int book) => $"/ui-test/borrowing/reserve-panel/{CatalogSeedIds.Book(book)}";

    private async Task<string> CookieAsync(Guid member) => await UiTestClient.CookieAsync(factory, Email(member), StaffSeedData.Password);

    private async Task<(HttpStatusCode Status, string Html, string SetCookies)> GetAsync(string cookie, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        var response = await UiTestClient.Anonymous(factory).SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), UiTestClient.SetCookies(response));
    }

    /// <summary>Opens the panel, posts its form like a browser and returns the response and every cookie so far.</summary>
    private async Task<(HttpResponseMessage Response, string Cookies)> ReserveAsync(Guid member, int book)
    {
        string auth = await CookieAsync(member);
        var (_, html, set) = await GetAsync(auth, Panel(book));
        // The panel does not offer the form when a copy is on the shelf; the token of any form of the host is as good.
        if (!html.Contains(ReserveButton))
            (_, html, set) = await GetAsync(auth, "/forbidden");
        string token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        string cookies = $"{auth}; {set}";

        var post = new HttpRequestMessage(HttpMethod.Post, "/reservations")
        {
            Content = new FormUrlEncodedContent([new("BookId", CatalogSeedIds.Book(book).ToString()), new("__RequestVerificationToken", token)]),
        };
        post.Headers.Add("Cookie", cookies);
        var response = await UiTestClient.Anonymous(factory).SendAsync(post);
        string afterPost = UiTestClient.SetCookies(response);
        return (response, afterPost.Length == 0 ? cookies : $"{cookies}; {afterPost}");
    }

    /// <summary>The alert the next page shows (Post/Redirect/Get): any page with the shared layout renders it.</summary>
    private async Task<string> NextPageAsync(string cookies) => (await GetAsync(cookies, "/forbidden")).Html;

    /// <summary>Every copy of the book is lent to someone else, so the book can only be reserved.</summary>
    private async Task LendAllCopiesAsync(int book, Guid borrower)
    {
        using var scope = factory.Services.CreateScope();
        var loans = scope.ServiceProvider.GetRequiredService<ILoanService>();
        for (int copy = 1; copy <= CatalogSeedData.CopiesPerBook[book]; copy++)
            await loans.IssueAsync(new IssueLoanRequest(borrower, CatalogSeedIds.CopyBarcode(book, copy)));
    }

    private async Task<List<Reservation>> ReservationsAsync(Guid member)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Reservations.AsNoTracking()
            .Where(r => r.StaffMemberId == member).ToListAsync();
    }

    private async Task<string> LimitsAsync(Guid member, int openLoans)
    {
        using var scope = factory.Services.CreateScope();
        var rules = await scope.ServiceProvider.GetRequiredService<IStaffMemberService>().GetLendingRulesAsync(member);
        return new ReservePanelModel(Guid.Empty, ReservePanelState.OnShelf) { OpenLoans = openLoans, Rules = rules }.LimitsText;
    }

    [Fact]
    public async Task A_copy_on_the_shelf_is_taken_at_the_desk_without_a_reserve_button()
    {
        var (status, html, _) = await GetAsync(await CookieAsync(Members.NarekSimonyan), Panel(1));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<section class=\"card\" aria-labelledby=\"reserve-panel-title\">", html);
        Assert.Contains(">Get this book</h2>", html);
        Assert.Contains("A copy is on the shelf. Take it at the library desk", html);
        Assert.DoesNotContain(ReserveButton, html);
        Assert.Contains(await LimitsAsync(Members.NarekSimonyan, 0), html);
        // No copy count and no next return date: Group 1's BookDetails shows them.
        Assert.DoesNotContain("copies", html);
    }

    [Fact]
    public async Task All_copies_out_offers_the_reservation_and_after_it_the_queue_place()
    {
        await LendAllCopiesAsync(44, Members.ErikZakaryan);

        var (_, before, _) = await GetAsync(await CookieAsync(Members.AnnaPetrosyan), Panel(44));
        Assert.Contains("All copies are out.", before);
        Assert.Contains(ReserveButton, before);
        Assert.Contains("action=\"/reservations\"", before);

        var (response, cookies) = await ReserveAsync(Members.AnnaPetrosyan, 44);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/catalog/books/{CatalogSeedIds.Book(44)}", response.Headers.Location!.OriginalString);
        var reservation = Assert.Single(await ReservationsAsync(Members.AnnaPetrosyan));
        Assert.Equal((CatalogSeedIds.Book(44), ReservationStatus.Waiting, 1), (reservation.BookId, reservation.Status, reservation.QueuePosition));

        string next = await NextPageAsync(cookies);
        Assert.Contains("alert alert-green", next);
        Assert.Contains("Book reserved.", next);
        Assert.Contains("You are 1st in the queue.", next);

        var (_, after, _) = await GetAsync(await CookieAsync(Members.AnnaPetrosyan), Panel(44));
        Assert.Contains("badge-grey", after);
        Assert.Contains("You reserved this book and are 1st in the queue.", after);
        Assert.DoesNotContain(ReserveButton, after);
    }

    [Fact]
    public async Task A_reservation_while_a_copy_is_available_is_refused_with_a_red_alert_BR_BOR_3()
    {
        var (response, cookies) = await ReserveAsync(Members.EvaHovsepyan, 2);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/catalog/books/{CatalogSeedIds.Book(2)}", response.Headers.Location!.OriginalString);
        Assert.Empty(await ReservationsAsync(Members.EvaHovsepyan));

        string next = await NextPageAsync(cookies);
        Assert.Contains("alert alert-red", next);
        Assert.Contains("The book was not reserved.", next);
        Assert.Contains(WebUtility.HtmlEncode(BorrowingErrors.ReservationCopyAvailable.Message), next);
    }

    [Fact]
    public async Task A_suspended_member_gets_the_reason_of_Staff_in_a_red_alert()
    {
        await LendAllCopiesAsync(45, Members.AlinaMirzoyan);

        var (response, cookies) = await ReserveAsync(Members.SonaVardanyan, 45);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(await ReservationsAsync(Members.SonaVardanyan));
        string next = await NextPageAsync(cookies);
        Assert.Contains("alert alert-red", next);
        Assert.Contains("The book was not reserved.", next);
    }

    [Fact]
    public async Task A_book_the_member_has_on_loan_says_so()
    {
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ILoanService>()
                .IssueAsync(new IssueLoanRequest(Members.TigranMelikyan, CatalogSeedIds.CopyBarcode(46, 1)));

        var (_, html, _) = await GetAsync(await CookieAsync(Members.TigranMelikyan), Panel(46));

        Assert.Contains("You have this book on loan.", html);
        Assert.DoesNotContain(ReserveButton, html);
        Assert.Contains(await LimitsAsync(Members.TigranMelikyan, 1), html);
    }

    [Fact]
    public async Task A_copy_held_for_the_member_shows_the_pickup_deadline()
    {
        DateTime readyUntil;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var reservation = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(47), Members.ManeGevorgyan, clock.UtcNow.AddDays(-3), 1);
            readyUntil = clock.UtcNow.AddDays(2);
            reservation.MarkReady(CatalogSeedIds.Copy(47, 1), readyUntil);
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync();
            readyUntil = clock.ToLocal(readyUntil);
        }

        var (_, html, _) = await GetAsync(await CookieAsync(Members.ManeGevorgyan), Panel(47));

        Assert.Contains("badge-green", html);
        Assert.Contains($"Pick it up before {readyUntil.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}.", html);
        Assert.DoesNotContain(ReserveButton, html);
    }

    [Fact]
    public async Task The_reserve_page_takes_only_a_post_with_the_antiforgery_token()
    {
        string auth = await CookieAsync(Members.ArmenBabayan);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(auth, "/reservations")).Status);

        var post = new HttpRequestMessage(HttpMethod.Post, "/reservations")
        {
            Content = new FormUrlEncodedContent([new("BookId", CatalogSeedIds.Book(48).ToString())]),
        };
        post.Headers.Add("Cookie", auth);
        Assert.Equal(HttpStatusCode.BadRequest, (await UiTestClient.Anonymous(factory).SendAsync(post)).StatusCode);
        Assert.Empty(await ReservationsAsync(Members.ArmenBabayan));
    }

    [Fact]
    public async Task The_panel_needs_a_signed_in_user()
    {
        var response = await UiTestClient.Anonymous(factory).GetAsync(Panel(1));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(112, "112th")]
    public void Queue_places_are_ordinals(int place, string text) => Assert.Equal(text, ReservePanelModel.Ordinal(place));

    [Theory]
    [InlineData(0, "no renewals.")]
    [InlineData(1, "up to 1 renewal.")]
    [InlineData(2, "up to 2 renewals.")]
    public void The_limits_line_follows_the_member_rules(int renewals, string end)
    {
        var model = new ReservePanelModel(Guid.Empty, ReservePanelState.OnShelf) { OpenLoans = 2, Rules = new LendingRules(3, 14, renewals) };

        Assert.Equal($"You have 2 of 3 books on loan. Loan period: 14 days, {end}", model.LimitsText);
    }
}
