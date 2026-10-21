using System.Net;
using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// <c>WaivePenaltyForm</c> by the UI guide (Part 1 "Group 3", demo <c>staff-member.html</c>) — one form per active
/// penalty record in the "Standing" card of <c>StaffBorrowingSummary</c>, reason required (BOR-08), library staff only;
/// and the barcode of a copy held for pickup in the "Loans and reservations" table. Every test uses its own members.
/// </summary>
public class WaivePenaltyPageTests(BorrowingUiHost factory) : IClassFixture<BorrowingUiHost>
{
    private const string Waive = "/penalties/waive";

    private static string StaffPage(Guid member) => $"/staff/{member}";

    private Task<UiBrowser> LibrarianAsync() => UiBrowser.SignInAsync(factory, Members.AramSargsyan);

    /// <summary>A returned loan of <paramref name="book"/> with one active penalty record of <paramref name="points"/>.</summary>
    private async Task<PenaltyRecord> PenaltyAsync(Guid member, int book, int points, string reason = "Late return: 2 overdue working day(s).")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var loan = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(book, 1), member, clock.UtcNow.AddDays(-40), 14);
        loan.Return(clock.UtcNow.AddDays(-20));
        var penalty = PenaltyRecord.Create(Guid.NewGuid(), member, loan.Id, points, reason, clock.UtcNow.AddDays(-20), clock.UtcNow.AddDays(70));
        db.Loans.Add(loan);
        db.PenaltyRecords.Add(penalty);
        await db.SaveChangesAsync();
        return penalty;
    }

    private async Task<PenaltyRecord> RecordAsync(Guid penaltyId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().PenaltyRecords.AsNoTracking().SingleAsync(p => p.Id == penaltyId);
    }

    private static string Field(PenaltyRecord penalty) => $"waive-{penalty.Id:N}";

    [Fact]
    public async Task Each_active_penalty_has_a_waiver_form_with_a_reason_field()
    {
        var first = await PenaltyAsync(Members.RubenHarutyunyan, 4, 2);
        var second = await PenaltyAsync(Members.RubenHarutyunyan, 5, 1, "Copy returned damaged.");

        var (_, html, _) = await (await LibrarianAsync()).GetAsync(StaffPage(Members.RubenHarutyunyan));

        Assert.Contains("<span class=\"stat-value\">3</span>", html);
        foreach (var (penalty, button) in new[] { (first, "Waive 2 points"), (second, "Waive 1 point") })
        {
            Assert.Contains($"<label for=\"{Field(penalty)}\">Reason for waiving</label>", html);
            Assert.Contains($"name=\"PenaltyId\" value=\"{penalty.Id}\"", html);
            Assert.Contains($">{button}</button>", html);
        }
        Assert.Contains("action=\"/penalties/waive\"", html);
        Assert.DoesNotContain("aria-invalid=\"true\"", html);
        Assert.DoesNotContain("style=\"", html);
    }

    [Fact]
    public async Task Waiving_closes_the_record_recalculates_the_points_and_comes_back_green_BOR_08()
    {
        var penalty = await PenaltyAsync(Members.AnahitMartirosyan, 6, 3);
        var browser = await LibrarianAsync();
        string page = StaffPage(Members.AnahitMartirosyan);

        var response = await browser.PostAsync(page, Waive, ("PenaltyId", penalty.Id.ToString()), ("Input.Reason", "  Courier delay  "), ("ReturnUrl", page));

        Assert.Equal(page, response.Headers.Location!.OriginalString);
        var record = await RecordAsync(penalty.Id);
        Assert.Equal((PenaltyRecordStatus.Waived, "Courier delay"), (record.Status, record.WaivedReason));
        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-green", next);
        Assert.Contains("Penalty waived.", next);
        Assert.Contains("3 points waived. The staff member's standing is recalculated.", next);
        Assert.Contains("<span class=\"stat-value\">0</span>", next);
        Assert.DoesNotContain(Field(penalty), next);
    }

    [Theory]
    [InlineData("", "A reason is required to waive the penalty.")]
    [InlineData("   ", "A reason is required to waive the penalty.")]
    [InlineData(null, "The reason may have at most 500 characters.")]
    public async Task A_refused_reason_is_shown_beside_the_field_of_that_form_and_nothing_is_waived(string? reason, string message)
    {
        Guid member = reason switch { "" => Members.ElenDanielyan, "   " => Members.SamvelPoghosyan, _ => Members.AniSahakyan };
        var penalty = await PenaltyAsync(member, 7 + (reason?.Length ?? 5), 2);
        var other = await PenaltyAsync(member, 12 + (reason?.Length ?? 5), 1);
        string typed = reason ?? new string('r', PenaltyRecord.ReasonMaxLength + 1);
        var browser = await LibrarianAsync();
        string page = StaffPage(member);

        var response = await browser.PostAsync(page, Waive, ("PenaltyId", penalty.Id.ToString()), ("Input.Reason", typed), ("ReturnUrl", page));

        Assert.Equal(page, response.Headers.Location!.OriginalString);
        Assert.Equal(PenaltyRecordStatus.Active, (await RecordAsync(penalty.Id)).Status);
        string next = await browser.FollowAsync(response);
        Assert.Matches($"<input id=\"{Field(penalty)}\"[^>]*aria-invalid=\"true\"", next);
        Assert.Contains($"<span class=\"error-text\">{message}</span>", next);
        Assert.DoesNotMatch($"<input id=\"{Field(other)}\"[^>]*aria-invalid", next);
        if (reason is null)
            Assert.Contains($"value=\"{typed}\"", next);   // what was typed stays in the field
        Assert.DoesNotContain("alert alert-green", next);

        // Shown once: the next visit has no error.
        Assert.DoesNotContain("aria-invalid=\"true\"", (await browser.GetAsync(page)).Html);
    }

    [Fact]
    public async Task A_record_that_is_no_longer_active_gets_a_red_alert()
    {
        var penalty = await PenaltyAsync(Members.LianaNalbandyan, 18, 2);
        var browser = await LibrarianAsync();
        string page = StaffPage(Members.LianaNalbandyan);
        await browser.PostAsync(page, Waive, ("PenaltyId", penalty.Id.ToString()), ("Input.Reason", "First"), ("ReturnUrl", page));

        var response = await browser.PostAsync(page, Waive, ("PenaltyId", penalty.Id.ToString()), ("Input.Reason", "Second"), ("ReturnUrl", page));

        string next = await browser.FollowAsync(response);
        Assert.Contains("alert alert-red", next);
        Assert.Contains("The penalty was not waived.", next);
        Assert.Contains(BorrowingErrors.PenaltyNotActive.Message, next);
        Assert.Equal("First", (await RecordAsync(penalty.Id)).WaivedReason);
    }

    [Fact]
    public async Task An_unknown_record_is_not_found_and_an_outside_return_url_falls_back_to_the_staff_list()
    {
        var browser = await LibrarianAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync("/desk", Waive, ("PenaltyId", Guid.NewGuid().ToString()), ("Input.Reason", "x"))).StatusCode);

        var penalty = await PenaltyAsync(Members.SofiaHambardzumyan, 19, 1);
        var response = await browser.PostAsync("/desk", Waive, ("PenaltyId", penalty.Id.ToString()), ("Input.Reason", "Desk closed"), ("ReturnUrl", "https://example.com/"));
        Assert.Equal("/staff", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_staff_member_cannot_waive_and_the_page_takes_only_a_post_with_the_antiforgery_token()
    {
        var penalty = await PenaltyAsync(Members.VahanStepanyan, 20, 2);
        var member = await UiBrowser.SignInAsync(factory, Members.VahanStepanyan);

        var forbidden = await member.PostAsync("/forbidden", Waive, ("PenaltyId", penalty.Id.ToString()), ("Input.Reason", "Mine"));
        Assert.Equal("/forbidden", forbidden.Headers.Location!.AbsolutePath);

        var librarian = await LibrarianAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await librarian.GetAsync(Waive)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await librarian.PostRawAsync(Waive, ("PenaltyId", penalty.Id.ToString()), ("Input.Reason", "x"))).StatusCode);
        Assert.Equal(PenaltyRecordStatus.Active, (await RecordAsync(penalty.Id)).Status);
    }

    [Fact]
    public async Task A_copy_held_for_pickup_shows_its_barcode_in_the_loans_and_reservations_table()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var reservation = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(21), Members.KarenAsatryan, clock.UtcNow.AddDays(-2), 1);
            reservation.MarkReady(CatalogSeedIds.Copy(21, 1), clock.UtcNow.AddDays(2));
            db.Reservations.Add(reservation);
            (await db.BookCopies.SingleAsync(c => c.Id == CatalogSeedIds.Copy(21, 1))).ChangeStatus(BookCopyStatus.Reserved);
            await db.SaveChangesAsync();
        }

        var (_, html, _) = await (await LibrarianAsync()).GetAsync(StaffPage(Members.KarenAsatryan));

        Assert.Contains($"<td class=\"nowrap\" data-label=\"Copy\">{CatalogSeedIds.CopyBarcode(21, 1)}</td>", html);
        Assert.Contains("<span class=\"badge badge-green\">Ready for pickup</span>", html);
    }
}
