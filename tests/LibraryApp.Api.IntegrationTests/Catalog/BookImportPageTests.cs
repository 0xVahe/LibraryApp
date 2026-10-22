using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Options;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-09 in the browser: the Import books page with the real host and database.</summary>
public class BookImportPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/catalog/import";
    private const string Header = "Title,Isbn,PublicationYear,Language,Category,Authors,Publisher,Description,CoverImagePath";

    [Fact]
    public async Task A_staff_member_gets_Forbidden()
    {
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
        using var forbidden = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task The_page_shows_the_upload_form_and_links_the_sample_file()
    {
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("enctype=\"multipart/form-data\"", html);
        Assert.Contains("name=\"Csv\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("href=\"/samples/book-import.csv\"", html);
        foreach (var column in Header.Split(','))
            Assert.Contains($"<td data-label=\"Column\">{column}</td>", html);

        using var sample = await client.GetAsync("/samples/book-import.csv");
        Assert.Equal(HttpStatusCode.OK, sample.StatusCode);
        Assert.Equal("text/csv", sample.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_sample_file_imports()
    {
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);
        var csv = await client.GetStringAsync("/samples/book-import.csv");
        var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;

        using var response = await UploadAsync(client, csv);

        var html = await FollowAsync(client, response);
        Assert.Contains($"Imported {rows} books.", html);
    }

    [Fact]
    public async Task A_valid_file_imports_every_book()
    {
        var (category, author) = await SeedNamesAsync();
        string first = NewIsbn(), second = NewIsbn();
        var csv = $"""
            {Header}
            Imported From The Page,{first},2020,en,{category},{author},,,library-covers/01.png
            "Second, With A Comma",{second},2021,hy,{category},{author},,"Text, with a comma",
            """;
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await UploadAsync(client, csv);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Route, response.Headers.Location!.OriginalString);
        var html = await FollowAsync(client, response);
        Assert.Contains("alert alert-green", html);
        Assert.Contains("Imported 2 books.", html);
        Assert.Equal(["library-covers/01.png", null], await CoverPathsAsync(first, second));
    }

    [Fact]
    public async Task An_invalid_file_shows_every_row_error_and_imports_nothing()
    {
        var (category, author) = await SeedNamesAsync();
        string valid = NewIsbn(), badCover = NewIsbn();
        var csv = $"""
            {Header}
            A Valid Row,{valid},2020,en,{category},{author},,,
            A Bad Row,not-an-isbn,2020,xx,{category},Nobody Known,,,
            A Bad Cover,{badCover},2020,en,{category},{author},,,https://example.com/cover.png
            """;
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await UploadAsync(client, csv);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Nothing was imported.", html);
        var errors = Regex.Matches(ErrorTable(html), "<tr>\\s*<td data-label=\"Row\">(\\d+)</td>\\s*<td data-label=\"Field\">([^<]+)</td>\\s*<td data-label=\"Message\">([^<]+)</td>")
            .Select(match => (Row: match.Groups[1].Value, Field: match.Groups[2].Value, Message: match.Groups[3].Value))
            .ToList();
        Assert.Contains(errors, error => error is { Row: "3", Field: "Isbn" });
        Assert.Contains(errors, error => error is { Row: "3", Field: "Language" });
        Assert.Contains(errors, error => error is { Row: "3", Field: "Authors" });
        Assert.Contains(("4", "CoverImagePath", CoverImagePathRules.Message), errors);
        Assert.DoesNotContain(errors, error => error.Row == "2");
        Assert.Empty(await CoverPathsAsync(valid, badCover));
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_with_a_clear_message()
    {
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        var limits = factory.Services.GetRequiredService<IOptions<CatalogImportOptions>>().Value;
        using var response = await UploadAsync(client, new string('x', (int)limits.MaxFileBytes + 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Nothing was imported.", html);
        Assert.Contains("<td data-label=\"Field\">File</td>", ErrorTable(html));
        Assert.Contains($"The file must be {limits.MaxFileSize} or smaller.", ErrorTable(html));
    }

    [Fact]
    public async Task Submitting_without_a_file_asks_for_one()
    {
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AramSargsyan);

        using var response = await PostAsync(client, new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("alert alert-red", html);
        Assert.Contains("Choose a CSV file to import.", html);
    }

    private Task<HttpClient> SignedInAsync(Guid staffId) => UiTestClient.SignedInAsync(factory,
        StaffSeedData.People.Single(person => person.StaffMemberId == staffId).Email, StaffSeedData.Password);

    /// <summary>The row errors table, the first table on the page after a refused file.</summary>
    private static string ErrorTable(string html) =>
        Regex.Match(html, "<table class=\"table\">.*?</table>", RegexOptions.Singleline).Value;

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string csv)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return PostAsync(client, new MultipartFormDataContent { { file, "Csv", "books.csv" } });
    }

    /// <summary>Posts the form with the antiforgery token and cookie of a fresh GET, as the browser does.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, MultipartFormDataContent content)
    {
        using var form = await client.GetAsync(Route);
        var token = Regex.Match(await form.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        content.Add(new StringContent(WebUtility.HtmlDecode(token)), "__RequestVerificationToken");
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = content };
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

    /// <summary>A category and an author with names no other test uses, so the name lookup is never ambiguous.</summary>
    private async Task<(string Category, string Author)> SeedNamesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = new Category(Guid.NewGuid(), $"Page import category {suffix}");
        var author = new Author(Guid.NewGuid(), $"Page import author {suffix}");
        db.AddRange(category, author);
        await db.SaveChangesAsync();
        return (category.Name, author.FullName);
    }

    /// <summary>The cover paths of the active books with these ISBNs, in the order of the ISBNs.</summary>
    private async Task<List<string?>> CoverPathsAsync(params string[] isbns)
    {
        using var scope = factory.Services.CreateScope();
        var books = await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Books.AsNoTracking()
            .Where(book => isbns.Contains(book.Isbn) && !book.IsDeleted)
            .Select(book => new { book.Isbn, book.CoverImagePath })
            .ToListAsync();
        return books.OrderBy(book => Array.IndexOf(isbns, book.Isbn)).Select(book => book.CoverImagePath).ToList();
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
