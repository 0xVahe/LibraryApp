using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-10 in the browser: the Suggest a book and Review suggestions pages with the real host and database.</summary>
public class BookSuggestionPagesTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string SuggestPath = "/catalog/suggestions";
    private const string ReviewPath = "/catalog/suggestions/review";

    [Fact]
    public async Task A_staff_member_sends_a_suggestion_and_sees_it_as_Pending()
    {
        var title = "Suggested on the page " + Guid.NewGuid().ToString("N")[..8];
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        using var response = await PostAsync(client, SuggestPath, new()
        {
            ["Input.Title"] = title, ["Input.AuthorName"] = "Page Author", ["Input.Isbn"] = "",
            ["Input.Reason"] = "Useful for the risk team.",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SuggestPath, response.Headers.Location!.OriginalString);
        var html = await FollowAsync(client, response);
        Assert.Contains("alert alert-green", html);
        var suggestion = await SuggestionAsync(title);
        Assert.Equal(StaffSeedIds.StaffMembers.AnnaPetrosyan, suggestion.StaffMemberId);
        Assert.Null(suggestion.Isbn);
        Assert.Contains("<span class=\"badge badge-grey\">Pending</span>", Row(html, suggestion.Id));
    }

    [Fact]
    public async Task An_empty_form_shows_the_validator_errors_next_to_the_fields()
    {
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        using var response = await PostAsync(client, SuggestPath, new()
        {
            ["Input.Title"] = "", ["Input.AuthorName"] = " ", ["Input.Isbn"] = "123", ["Input.Reason"] = "",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("must not be empty", FieldError(html, "title-error"));
        Assert.Contains("must not be empty", FieldError(html, "author-error"));
        Assert.Contains("must not be empty", FieldError(html, "reason-error"));
        Assert.Equal("ISBN must be a valid ISBN-10 or ISBN-13.", FieldError(html, "isbn-error"));
        Assert.Contains("id=\"Input_Title\"", html);
        Assert.Matches("<input[^>]*aria-invalid=\"true\"[^>]*id=\"Input_Title\"|<input[^>]*id=\"Input_Title\"[^>]*aria-invalid=\"true\"", html);
    }

    [Theory]
    [InlineData("aram.sargsyan@ameriabank.am")]   // Librarian
    [InlineData("anna.petrosyan@ameriabank.am")]  // Staff Member
    public async Task Only_the_Library_Administrator_opens_the_review_page(string email)
    {
        using var client = await UiTestClient.SignedInAsync(factory, email, StaffSeedData.Password);

        using var response = await client.GetAsync(ReviewPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
        using var forbidden = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Rejecting_without_a_comment_shows_the_error_and_keeps_the_suggestion_Pending()
    {
        var id = await AddSuggestionAsync("Reject without comment");
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);

        using var response = await PostAsync(client, ReviewPath + "?handler=Reject", new()
        {
            ["id"] = id.ToString(), ["Comment"] = "  ",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("The suggestion was not rejected.", html);
        Assert.Contains("must not be empty", FieldError(Row(html, id), $"comment-error-{id}"));
        Assert.Contains("aria-invalid=\"true\"", Row(html, id));
        Assert.Equal(BookSuggestionStatus.Pending, (await FindAsync(id)).Status);
    }

    [Fact]
    public async Task The_administrator_approves_and_rejects_and_the_staff_member_sees_the_answer()
    {
        var approvedId = await AddSuggestionAsync("To approve");
        var rejectedId = await AddSuggestionAsync("To reject");
        using var admin = await SignedInAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);

        using (var approve = await PostAsync(admin, ReviewPath + "?handler=Approve&status=Pending", new() { ["id"] = approvedId.ToString() }))
        {
            Assert.Equal(ReviewPath + "?status=Pending", approve.Headers.Location!.OriginalString);
            var pending = await FollowAsync(admin, approve);
            Assert.Contains("Suggestion approved.", pending);
            Assert.Empty(Row(pending, approvedId));
            Assert.NotEmpty(Row(pending, rejectedId));
        }

        using (var reject = await PostAsync(admin, ReviewPath + "?handler=Reject", new()
        {
            ["id"] = rejectedId.ToString(), ["Comment"] = "We already have a newer edition.",
        }))
        {
            var all = await FollowAsync(admin, reject);
            Assert.Contains("<span class=\"badge badge-green\">Approved</span>", Row(all, approvedId));
            Assert.Contains("<span class=\"badge badge-red\">Rejected</span>", Row(all, rejectedId));
            Assert.Contains("We already have a newer edition.", Row(all, rejectedId));
        }

        using var owner = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);
        var mine = await owner.GetStringAsync(SuggestPath);
        Assert.Contains("<span class=\"badge badge-green\">Approved</span>", Row(mine, approvedId));
        Assert.Contains("We already have a newer edition.", Row(mine, rejectedId));

        using var librarian = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);
        Assert.Empty(Row(await librarian.GetStringAsync(SuggestPath), approvedId));   // only my own suggestions
    }

    private Task<HttpClient> SignedInAsync(Guid staffId) => UiTestClient.SignedInAsync(factory,
        StaffSeedData.People.Single(person => person.StaffMemberId == staffId).Email, StaffSeedData.Password);

    /// <summary>The table row of a suggestion, or an empty string when the page does not list it.</summary>
    private static string Row(string html, Guid id) =>
        Regex.Match(html, $"<tr id=\"suggestion-{id}\">.*?</tr>", RegexOptions.Singleline).Value;

    /// <summary>The text of the error span with this id.</summary>
    private static string FieldError(string html, string id) =>
        Regex.Match(html, $"<span id=\"{id}\"[^>]*>([^<]*)</span>").Groups[1].Value;

    /// <summary>Posts a form with the antiforgery token and cookie of a fresh GET of the Suggest a book page, as the browser does.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, Dictionary<string, string> fields)
    {
        using var form = await client.GetAsync(SuggestPath);
        var token = Regex.Match(await form.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("Cookie", Cookies(client, form));
        return await client.SendAsync(request);
    }

    private static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage redirect)
    {
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, redirect.Headers.Location);
        request.Headers.Add("Cookie", Cookies(client, redirect));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Cookies(HttpClient client, HttpResponseMessage response) =>
        string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie")) + "; " + UiTestClient.SetCookies(response);

    /// <summary>A Pending suggestion of Anna Petrosyan, written straight to the database.</summary>
    private async Task<Guid> AddSuggestionAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var suggestion = new BookSuggestion(Guid.NewGuid(), StaffSeedIds.StaffMembers.AnnaPetrosyan,
            $"{title} {Guid.NewGuid().ToString("N")[..8]}", "Page Author", null, "Useful for the team.", DateTime.UtcNow);
        db.BookSuggestions.Add(suggestion);
        await db.SaveChangesAsync();
        return suggestion.Id;
    }

    private async Task<BookSuggestion> FindAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().BookSuggestions.AsNoTracking()
            .SingleAsync(suggestion => suggestion.Id == id);
    }

    private async Task<BookSuggestion> SuggestionAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().BookSuggestions.AsNoTracking()
            .SingleAsync(suggestion => suggestion.Title == title);
    }
}
