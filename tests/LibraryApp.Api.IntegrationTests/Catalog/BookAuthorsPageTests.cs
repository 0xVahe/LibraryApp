using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

public class BookAuthorsPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Librarian = "aram.sargsyan@ameriabank.am";
    private const string StaffMember = "anna.petrosyan@ameriabank.am";
    private const string AuthorsPath = "/catalog/books/authors";

    private static string EditPath(Guid bookId) => $"/catalog/books/{bookId}/edit";

    private async Task<(Book Book, Author First, Author Second, Author Third)> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = new Category(Guid.NewGuid(), $"Authors page category {suffix}");
        var first = new Author(Guid.NewGuid(), $"Same name {suffix}");
        var second = new Author(Guid.NewGuid(), first.FullName);
        var third = new Author(Guid.NewGuid(), $"Other author {suffix}");
        var digits = "979" + Random.Shared.NextInt64(0, 1_000_000_000).ToString("D9");
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
            sum += (digits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        var isbn = digits + (10 - sum % 10) % 10;
        var book = new Book(Guid.NewGuid(), $"Authors page book {suffix}", isbn, 2020, "en", null, category.Id, null);
        db.AddRange(category, first, second, third, book,
            new BookAuthor(book.Id, first.Id), new BookAuthor(book.Id, third.Id));
        await db.SaveChangesAsync();
        return (book, first, second, third);
    }

    private async Task<Guid[]> SavedAuthorIdsAsync(Guid bookId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().BookAuthors
            .Where(link => link.BookId == bookId).Select(link => link.AuthorId).ToArrayAsync();
    }

    private static string AuthorSelect(string html)
    {
        var select = Regex.Match(html, "<select id=\"book-authors\".*?</select>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(select);
        return select;
    }

    private static string Option(string select, Guid id)
    {
        var option = Regex.Match(select, $"<option\\b[^>]*value=\"{id}\"[^>]*>.*?</option>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(option);
        return option;
    }

    private async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid bookId, Guid[] authorIds,
        bool staffMember = false, bool ajax = false)
    {
        // Staff cannot open Edit; the layout on Forbidden issues a token for that same signed-in user.
        using var page = await client.GetAsync(staffMember ? "/forbidden" : EditPath(bookId));
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var cookies = $"{client.DefaultRequestHeaders.GetValues("Cookie").Single()}; {UiTestClient.SetCookies(page)}";
        var fields = authorIds.Select(id => new KeyValuePair<string, string>("AuthorIds", id.ToString()))
            .Append(new("BookId", bookId.ToString()))
            .Append(new("__RequestVerificationToken", token));
        using var post = new HttpRequestMessage(HttpMethod.Post, AuthorsPath)
        {
            Content = new FormUrlEncodedContent(fields),
        };
        post.Headers.Add("Cookie", cookies);
        // The cookie handler returns 403 for this header instead of redirecting to /forbidden.
        if (ajax)
            post.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await client.SendAsync(post);
    }

    [Fact]
    public async Task Current_authors_are_preselected_by_ID_even_when_names_match()
    {
        var (book, first, second, third) = await SeedAsync();
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        var html = await client.GetStringAsync(EditPath(book.Id));

        var select = AuthorSelect(html);
        Assert.Contains("multiple", select);
        Assert.Contains("name=\"AuthorIds\"", select);
        Assert.Contains("selected", Option(select, first.Id));
        Assert.DoesNotContain("selected", Option(select, second.Id));
        Assert.Contains("selected", Option(select, third.Id));
        Assert.Contains($">{first.FullName}</option>", Option(select, first.Id));
        Assert.Contains($">{second.FullName}</option>", Option(select, second.Id));
        Assert.Contains($"action=\"{AuthorsPath}\"", html);
        Assert.Contains($"name=\"BookId\" value=\"{book.Id}\"", html);
        Assert.Contains("href=\"/catalog/authors\"", html);
        foreach (Match form in Regex.Matches(html, "<form\\b.*?</form>", RegexOptions.Singleline))
        {
            Assert.Single(Regex.Matches(form.Value, "<form\\b").Cast<Match>());
            Assert.Contains("name=\"__RequestVerificationToken\"", form.Value);
        }
    }

    [Fact]
    public async Task Saving_replaces_deselected_authors_and_retains_selected_authors()
    {
        var (book, first, second, third) = await SeedAsync();
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        using var withoutToken = await client.PostAsync(AuthorsPath, new FormUrlEncodedContent(
        [
            new("BookId", book.Id.ToString()),
            new("AuthorIds", second.Id.ToString()),
        ]));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        var unchanged = await SavedAuthorIdsAsync(book.Id);
        Assert.Equal(2, unchanged.Length);
        Assert.Contains(first.Id, unchanged);
        Assert.Contains(third.Id, unchanged);

        using var response = await PostAsync(client, book.Id, [second.Id, third.Id]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(EditPath(book.Id), response.Headers.Location!.OriginalString);
        var saved = await SavedAuthorIdsAsync(book.Id);
        Assert.Equal(2, saved.Length);
        Assert.Contains(second.Id, saved);
        Assert.Contains(third.Id, saved);
        Assert.DoesNotContain(first.Id, saved);
        var select = AuthorSelect(await client.GetStringAsync(EditPath(book.Id)));
        Assert.DoesNotContain("selected", Option(select, first.Id));
        Assert.Contains("selected", Option(select, second.Id));
        Assert.Contains("selected", Option(select, third.Id));
    }

    [Fact]
    public async Task Missing_selection_renders_Edit_with_BR_CAT_5_and_leaves_saved_authors_unchanged()
    {
        var (book, first, second, third) = await SeedAsync();
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        using var response = await PostAsync(client, book.Id, []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(HtmlEncoder.Default.Encode(CatalogErrors.BookAuthorsRequired.Message), html);
        Assert.Contains($"value=\"{book.Isbn}\"", html);
        Assert.Contains($"action=\"{EditPath(book.Id)}\"", html);
        Assert.Contains($"action=\"{EditPath(book.Id)}?handler=Delete\"", html);
        var select = AuthorSelect(html);
        Assert.DoesNotContain("selected", select);
        Option(select, first.Id);
        Option(select, second.Id);
        Option(select, third.Id);
        var saved = await SavedAuthorIdsAsync(book.Id);
        Assert.Equal(2, saved.Length);
        Assert.Contains(first.Id, saved);
        Assert.Contains(third.Id, saved);
    }

    [Fact]
    public async Task Staff_member_post_with_valid_selection_and_antiforgery_is_forbidden()
    {
        var (book, first, second, third) = await SeedAsync();
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        using var response = await PostAsync(client, book.Id, [second.Id], staffMember: true, ajax: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // Normal form posts follow the shared cookie scheme to the existing HTTP 403 page.
        using var browserResponse = await PostAsync(client, book.Id, [second.Id], staffMember: true);
        Assert.Equal(HttpStatusCode.Redirect, browserResponse.StatusCode);
        Assert.Equal("/forbidden", browserResponse.Headers.Location!.AbsolutePath);
        using var forbidden = await client.GetAsync(browserResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var saved = await SavedAuthorIdsAsync(book.Id);
        Assert.Equal(2, saved.Length);
        Assert.Contains(first.Id, saved);
        Assert.Contains(third.Id, saved);
    }
}
