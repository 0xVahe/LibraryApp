using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-04: the copies panel of the Edit book page and its Copies page, with the real host and database.</summary>
public class CopiesPanelPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public async Task The_panel_lists_every_copy_and_offers_only_the_allowed_status_changes()
    {
        var bookId = await CreateBookAsync();
        var prefix = NewPrefix();
        var available = await AddCopyAsync(bookId, $"{prefix}-A");
        var repair = await AddCopyAsync(bookId, $"{prefix}-R", BookCopyStatus.UnderRepair);
        var lost = await AddCopyAsync(bookId, $"{prefix}-L", BookCopyStatus.Lost);
        var withdrawn = await AddCopyAsync(bookId, $"{prefix}-W", BookCopyStatus.Withdrawn);
        var onLoan = await AddCopyAsync(bookId, $"{prefix}-O");
        using (var scope = factory.Services.CreateScope())
        {
            // SetCopyStatusAsync runs inside Borrowing's unit of work, which saves; here the test saves itself.
            await scope.ServiceProvider.GetRequiredService<ICatalogService>().SetCopyStatusAsync(onLoan.Id, BookCopyStatus.OnLoan);
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().SaveChangesAsync();
        }
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        var html = await client.GetStringAsync(EditPath(bookId));

        Assert.Equal(["UnderRepair", "Lost", "Withdrawn"], StatusButtons(html, available.Id));
        Assert.Equal(["Available", "Lost", "Withdrawn"], StatusButtons(html, repair.Id));
        Assert.Empty(StatusButtons(html, lost.Id));
        Assert.Empty(StatusButtons(html, withdrawn.Id));
        Assert.Empty(StatusButtons(html, onLoan.Id));
        Assert.Contains("badge badge-red\">Lost<", Row(html, lost.Id));
        Assert.Contains("badge badge-grey\">Withdrawn<", Row(html, withdrawn.Id));
        Assert.Contains("badge badge-blue\">On loan<", Row(html, onLoan.Id));
        Assert.Contains("name=\"ShelfLocation\"", Row(html, available.Id));
        Assert.DoesNotContain("name=\"ShelfLocation\"", Row(html, lost.Id));   // left the collection: no edit form
    }

    [Fact]
    public async Task Adding_a_copy_comes_back_to_the_Edit_page_with_an_alert()
    {
        var bookId = await CreateBookAsync();
        var barcode = NewPrefix() + "-new";
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await PostAsync(client, bookId, "Add", new()
        {
            ["Barcode"] = barcode, ["ShelfLocation"] = "S8-01", ["Condition"] = "New",
        });

        var html = await FollowAsync(client, response, bookId);
        Assert.Contains($"Copy {barcode.ToUpperInvariant()} added.", html);
        var copy = Assert.Single(await CopiesAsync(bookId));
        Assert.Equal((barcode.ToUpperInvariant(), BookCopyStatus.Available, BookCopyCondition.New, "S8-01"),
            (copy.Barcode, copy.Status, copy.Condition, copy.ShelfLocation));
        Assert.Contains($"<td data-label=\"Barcode\">{copy.Barcode}</td>", html);
    }

    [Fact]
    public async Task A_duplicate_barcode_shows_the_error_and_adds_nothing()
    {
        var bookId = await CreateBookAsync();
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await PostAsync(client, bookId, "Add", new()
        {
            ["Barcode"] = CatalogSeedIds.CopyBarcode(1, 1), ["Condition"] = "Good",
        });

        var html = WebUtility.HtmlDecode(await FollowAsync(client, response, bookId));
        Assert.Contains("alert alert-red", html);
        Assert.Contains("The copy was not added.", html);
        Assert.Contains(CatalogErrors.CopyBarcodeDuplicate.Message, html);
        Assert.Empty(await CopiesAsync(bookId));
    }

    [Fact]
    public async Task An_invalid_barcode_is_refused_with_the_validation_message()
    {
        var bookId = await CreateBookAsync();
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await PostAsync(client, bookId, "Add", new() { ["Barcode"] = "has space", ["Condition"] = "Good" });

        var html = WebUtility.HtmlDecode(await FollowAsync(client, response, bookId));
        Assert.Contains("The copy was not added.", html);
        Assert.Contains("The barcode may contain only letters, digits and '-'", html);
        Assert.Empty(await CopiesAsync(bookId));
    }

    [Fact]
    public async Task Shelf_and_condition_are_saved()
    {
        var bookId = await CreateBookAsync();
        var copy = await AddCopyAsync(bookId, NewPrefix() + "-E");
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);

        using var response = await PostAsync(client, bookId, "Update", new()
        {
            ["copyId"] = copy.Id.ToString(), ["ShelfLocation"] = "S2-09", ["Condition"] = "Poor",
        });

        var html = await FollowAsync(client, response, bookId);
        Assert.Contains($"Copy {copy.Barcode} saved.", html);
        var saved = Assert.Single(await CopiesAsync(bookId));
        Assert.Equal(("S2-09", BookCopyCondition.Poor), (saved.ShelfLocation, saved.Condition));
    }

    [Fact]
    public async Task A_copy_goes_to_repair_and_comes_back()
    {
        var bookId = await CreateBookAsync();
        var copy = await AddCopyAsync(bookId, NewPrefix() + "-S");
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var toRepair = await PostAsync(client, bookId, "Status", new() { ["copyId"] = copy.Id.ToString(), ["status"] = "UnderRepair" });
        Assert.Contains($"Copy {copy.Barcode} is now under repair.", await FollowAsync(client, toRepair, bookId));
        using var back = await PostAsync(client, bookId, "Status", new() { ["copyId"] = copy.Id.ToString(), ["status"] = "Available" });
        Assert.Contains($"Copy {copy.Barcode} is now available.", await FollowAsync(client, back, bookId));

        Assert.Equal(BookCopyStatus.Available, Assert.Single(await CopiesAsync(bookId)).Status);
    }

    [Fact]
    public async Task A_status_the_service_refuses_shows_the_error()
    {
        var bookId = await CreateBookAsync();
        var copy = await AddCopyAsync(bookId, NewPrefix() + "-X", BookCopyStatus.Withdrawn);
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await PostAsync(client, bookId, "Status", new() { ["copyId"] = copy.Id.ToString(), ["status"] = "Available" });

        var html = await FollowAsync(client, response, bookId);
        Assert.Contains("alert alert-red", html);
        Assert.Contains("The status was not changed.", html);
        Assert.Equal(BookCopyStatus.Withdrawn, Assert.Single(await CopiesAsync(bookId)).Status);
    }

    [Fact]
    public async Task A_new_copy_for_a_waiting_reader_comes_back_Reserved()
    {
        var bookId = await CreateBookAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.Add(Reservation.Create(Guid.NewGuid(), bookId, StaffSeedIds.StaffMembers.AnnaPetrosyan, DateTime.UtcNow, 1));
            await db.SaveChangesAsync();
        }
        var barcode = NewPrefix() + "-Q";
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await PostAsync(client, bookId, "Add", new() { ["Barcode"] = barcode, ["Condition"] = "Good" });

        var html = await FollowAsync(client, response, bookId);
        Assert.Contains("the copy is now held for them (Reserved)", html);
        Assert.Equal(BookCopyStatus.Reserved, Assert.Single(await CopiesAsync(bookId)).Status);
    }

    [Fact]
    public async Task A_staff_member_gets_Forbidden_and_nothing_changes()
    {
        var bookId = await CreateBookAsync();
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        // The Forbidden page has the real layout, so it gives this user an antiforgery token and cookie.
        using var response = await PostAsync(client, bookId, "Add", new() { ["Barcode"] = NewPrefix() + "-F", ["Condition"] = "Good" },
            tokenPath: "/forbidden");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
        Assert.Empty(await CopiesAsync(bookId));
    }

    private Task<HttpClient> SignedInAsync(Guid staffId) => UiTestClient.SignedInAsync(factory,
        StaffSeedData.People.Single(person => person.StaffMemberId == staffId).Email, StaffSeedData.Password);

    private static string EditPath(Guid bookId) => $"/catalog/books/{bookId}/edit";

    private static string NewPrefix() => "CP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>The table row of a copy in the panel.</summary>
    private static string Row(string html, Guid copyId) =>
        Regex.Match(html, $"<tr id=\"copy-{copyId}\">.*?</tr>", RegexOptions.Singleline).Value;

    /// <summary>The statuses the status buttons of a copy would set.</summary>
    private static string[] StatusButtons(string html, Guid copyId) =>
        Regex.Matches(Row(html, copyId), "name=\"status\" value=\"([A-Za-z]+)\"").Select(match => match.Groups[1].Value).ToArray();

    /// <summary>Posts a form of the panel with the antiforgery token and cookie of a fresh GET, as the browser does.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid bookId, string handler,
        Dictionary<string, string> fields, string? tokenPath = null)
    {
        using var form = await client.GetAsync(tokenPath ?? EditPath(bookId));
        var token = Regex.Match(await form.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/catalog/books/{bookId}/copies?handler={handler}")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Add("Cookie", Cookies(client, form));
        return await client.SendAsync(request);
    }

    /// <summary>Follows the redirect back to the Edit page and returns it, with the alert.</summary>
    private static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage redirect, Guid bookId)
    {
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal(EditPath(bookId), redirect.Headers.Location!.OriginalString);
        using var request = new HttpRequestMessage(HttpMethod.Get, redirect.Headers.Location);
        request.Headers.Add("Cookie", Cookies(client, redirect));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Cookies(HttpClient client, HttpResponseMessage response) =>
        string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie")) + "; " + UiTestClient.SetCookies(response);

    private async Task<IReadOnlyList<BookCopyDto>> CopiesAsync(Guid bookId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBookCopyService>().GetCopiesAsync(bookId);
    }

    private async Task<BookCopyDto> AddCopyAsync(Guid bookId, string barcode, BookCopyStatus? status = null)
    {
        using var scope = factory.Services.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<IBookCopyService>();
        var copy = await copies.AddCopyAsync(bookId, new AddCopyRequest(barcode, BookCopyCondition.Good, "S1-01"));
        return status is { } next ? await copies.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(next)) : copy;
    }

    /// <summary>A new book without copies, in a seeded category, by a seeded author.</summary>
    private async Task<Guid> CreateBookAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBookService>().CreateAsync(new CreateBookRequest(
            "Copies Panel " + Guid.NewGuid().ToString("N")[..6], NewIsbn(), 2020, "en", null, CatalogSeedIds.Category(8), null,
            [CatalogSeedData.Authors[0].Id]));
    }

    private static string NewIsbn()
    {
        var digits = "979" + Random.Shared.NextInt64(0, 1_000_000_000).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (digits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return digits + (10 - sum % 10) % 10;
    }
}
