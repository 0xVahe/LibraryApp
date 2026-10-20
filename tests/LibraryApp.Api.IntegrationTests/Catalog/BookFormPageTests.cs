using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-01 pages: New book and Edit book, signed in with the UI cookie as seeded users.</summary>
public class BookFormPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Librarian = "aram.sargsyan@ameriabank.am";
    private const string StaffMember = "anna.petrosyan@ameriabank.am";

    private static string NewIsbn()
    {
        var digits = "979" + Random.Shared.NextInt64(0, 1_000_000_000).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (digits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return digits + (10 - sum % 10) % 10;
    }

    /// <summary>A category, an author and (optionally) a book of them, with names no other test uses.</summary>
    private async Task<(Guid CategoryId, Guid AuthorId, Guid BookId, string Isbn)> SeedAsync(bool withOnLoanCopy = false,
        string? coverImagePath = null, string language = "en")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = new Category(Guid.NewGuid(), $"Page category {suffix}");
        var author = new Author(Guid.NewGuid(), $"Page author {suffix}");
        var isbn = NewIsbn();
        var book = new Book(Guid.NewGuid(), $"Page book {suffix}", isbn, 2020, language, null, category.Id, null, coverImagePath);
        db.AddRange(category, author, book, new BookAuthor(book.Id, author.Id));
        if (withOnLoanCopy)
        {
            var copy = new BookCopy(Guid.NewGuid(), book.Id, $"P{suffix}", BookCopyCondition.Good, null);
            copy.ChangeStatus(BookCopyStatus.OnLoan);
            db.Add(copy);
        }
        await db.SaveChangesAsync();
        return (category.Id, author.Id, book.Id, isbn);
    }

    /// <summary>GET the page, then POST the form with the page's antiforgery token and cookie, as a browser does.</summary>
    private async Task<HttpResponseMessage> PostFormAsync(string email, string path, string? handler,
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        var authCookie = await UiTestClient.CookieAsync(factory, email, StaffSeedData.Password);
        var client = UiTestClient.Anonymous(factory);

        var pageRequest = new HttpRequestMessage(HttpMethod.Get, path);
        pageRequest.Headers.Add("Cookie", authCookie);
        var page = await client.SendAsync(pageRequest);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        var postPath = handler is null ? path : $"{path}?handler={handler}";
        var post = new HttpRequestMessage(HttpMethod.Post, postPath)
        {
            Content = new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token))),
        };
        post.Headers.Add("Cookie", $"{authCookie}; {UiTestClient.SetCookies(page)}");
        return await client.SendAsync(post);
    }

    private static IEnumerable<KeyValuePair<string, string>> BookFields(Guid categoryId, string isbn, string title = "Page Test Book") =>
    [
        new("Input.Title", title),
        new("Input.Isbn", isbn),
        new("Input.PublicationYear", "2021"),
        new("Input.Language", "en"),
        new("Input.CategoryId", categoryId.ToString()),
        new("Input.PublisherId", ""),
        new("Input.Description", "Created by a page test"),
    ];

    [Theory]
    [InlineData("/catalog/books/new")]
    [InlineData("/catalog/books/{0}/edit")]
    public async Task A_staff_member_is_sent_to_the_Forbidden_page(string path)
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        var response = await client.GetAsync(string.Format(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task A_librarian_opens_the_New_book_form_with_authors_and_categories()
    {
        var (categoryId, authorId, _, _) = await SeedAsync();
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        var html = await client.GetStringAsync("/catalog/books/new");

        Assert.Contains("name=\"Input.Title\"", html);
        Assert.Contains($"value=\"{categoryId}\"", html);
        Assert.Contains($"value=\"{authorId}\"", html);
        Assert.Contains("No publisher", html);
    }

    [Fact]
    public async Task Adding_a_book_goes_to_its_Edit_page()
    {
        var (categoryId, authorId, _, _) = await SeedAsync();
        var isbn = NewIsbn();

        var response = await PostFormAsync(Librarian, "/catalog/books/new", null,
            BookFields(categoryId, isbn).Append(new("Input.AuthorIds", authorId.ToString())));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Matches("^/catalog/books/[0-9a-f-]{36}/edit$", response.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.True(await db.Books.AnyAsync(x => x.Isbn == isbn));
    }

    [Fact]
    public async Task Empty_fields_stay_on_the_page_with_errors_next_to_them()
    {
        var response = await PostFormAsync(Librarian, "/catalog/books/new", null,
        [
            new("Input.Title", ""),
            new("Input.Isbn", "not-an-isbn"),
            new("Input.Language", "en"),
        ]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Contains("ISBN must be a valid ISBN-10 or ISBN-13.", html);
    }

    [Fact]
    public async Task A_duplicate_ISBN_is_shown_on_the_page()
    {
        var (categoryId, authorId, _, existingIsbn) = await SeedAsync();

        var response = await PostFormAsync(Librarian, "/catalog/books/new", null,
            BookFields(categoryId, existingIsbn).Append(new("Input.AuthorIds", authorId.ToString())));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(HtmlEncoder.Default.Encode(CatalogErrors.BookIsbnDuplicate.Message), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_Edit_page_shows_and_saves_the_book()
    {
        var (categoryId, _, bookId, isbn) = await SeedAsync();
        var path = $"/catalog/books/{bookId}/edit";

        using (var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password))
            Assert.Contains($"value=\"{isbn}\"", await client.GetStringAsync(path));

        var response = await PostFormAsync(Librarian, path, null, BookFields(categoryId, isbn, "Renamed Page Book"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(path, response.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.Equal("Renamed Page Book", (await db.Books.SingleAsync(x => x.Id == bookId)).Title);
    }

    [Fact]
    public async Task A_language_the_form_does_not_offer_stays_selected_on_the_Edit_page()
    {
        // The API accepts any ISO 639-1 code; without its own option the select would fall back to its first choice.
        var (_, _, bookId, _) = await SeedAsync(language: "fr");
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        var html = await client.GetStringAsync($"/catalog/books/{bookId}/edit");

        var select = Regex.Match(html, "<select[^>]*name=\"Input.Language\"[^>]*>(.*?)</select>", RegexOptions.Singleline).Value;
        var selected = Regex.Matches(select, "<option(?=[^>]*selected)[^>]*value=\"([^\"]*)\"").Cast<Match>()
            .Select(match => match.Groups[1].Value);
        Assert.Equal(new[] { "fr" }, selected);
    }

    [Fact]
    public async Task Saving_the_Edit_form_keeps_the_cover()
    {
        const string cover = "library-covers/02.png";
        var (categoryId, _, bookId, isbn) = await SeedAsync(coverImagePath: cover);
        var path = $"/catalog/books/{bookId}/edit";

        using (var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password))
            Assert.Contains($"value=\"{cover}\"", await client.GetStringAsync(path));

        var response = await PostFormAsync(Librarian, path, null,
            BookFields(categoryId, isbn, "Renamed Book With Cover").Append(new("Input.CoverImagePath", cover)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var book = await FindBookAsync(bookId);
        Assert.Equal("Renamed Book With Cover", book.Title);
        Assert.Equal(cover, book.CoverImagePath);
    }

    [Fact]
    public async Task Clearing_the_cover_field_removes_the_cover()
    {
        var (categoryId, _, bookId, isbn) = await SeedAsync(coverImagePath: "library-covers/02.png");

        var response = await PostFormAsync(Librarian, $"/catalog/books/{bookId}/edit", null,
            BookFields(categoryId, isbn).Append(new("Input.CoverImagePath", "")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await FindBookAsync(bookId)).CoverImagePath);
    }

    [Fact]
    public async Task An_invalid_cover_path_is_shown_next_to_the_field_and_the_book_is_not_changed()
    {
        const string cover = "library-covers/02.png";
        var (categoryId, _, bookId, isbn) = await SeedAsync(coverImagePath: cover);

        var response = await PostFormAsync(Librarian, $"/catalog/books/{bookId}/edit", null,
            BookFields(categoryId, isbn, "Not Saved").Append(new("Input.CoverImagePath", "https://example.com/cover.png")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(HtmlEncoder.Default.Encode(CoverImagePathRules.Message), html);
        Assert.Matches("<input[^>]*aria-invalid=\"true\"[^>]*id=\"Input_CoverImagePath\"|<input[^>]*id=\"Input_CoverImagePath\"[^>]*aria-invalid=\"true\"", html);
        var book = await FindBookAsync(bookId);
        Assert.NotEqual("Not Saved", book.Title);
        Assert.Equal(cover, book.CoverImagePath);
    }

    [Fact]
    public async Task A_new_book_is_added_with_its_cover()
    {
        var (categoryId, authorId, _, _) = await SeedAsync();
        var isbn = NewIsbn();

        var response = await PostFormAsync(Librarian, "/catalog/books/new", null, BookFields(categoryId, isbn)
            .Append(new("Input.AuthorIds", authorId.ToString()))
            .Append(new("Input.CoverImagePath", " library-covers/03.png ")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.Equal("library-covers/03.png", (await db.Books.SingleAsync(x => x.Isbn == isbn)).CoverImagePath);
    }

    [Fact]
    public async Task An_unknown_book_shows_the_Not_found_page()
    {
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        var response = await client.GetAsync($"/catalog/books/{Guid.NewGuid()}/edit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_needs_the_confirmation_box()
    {
        var (_, _, bookId, _) = await SeedAsync();

        var response = await PostFormAsync(Librarian, $"/catalog/books/{bookId}/edit", "Delete", []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.False((await db.Books.SingleAsync(x => x.Id == bookId)).IsDeleted);
    }

    [Fact]
    public async Task A_book_with_a_copy_on_loan_is_not_deleted_and_the_reason_is_shown()
    {
        var (_, _, bookId, _) = await SeedAsync(withOnLoanCopy: true);

        var response = await PostFormAsync(Librarian, $"/catalog/books/{bookId}/edit", "Delete", [new("confirmDelete", "true")]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(HtmlEncoder.Default.Encode(CatalogErrors.BookHasCopiesInCirculation.Message), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_confirmed_delete_goes_back_to_the_Catalog()
    {
        var (_, _, bookId, _) = await SeedAsync();

        var response = await PostFormAsync(Librarian, $"/catalog/books/{bookId}/edit", "Delete", [new("confirmDelete", "true")]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/catalog", response.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.True((await db.Books.SingleAsync(x => x.Id == bookId)).IsDeleted);
    }

    private async Task<Book> FindBookAsync(Guid bookId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Books.AsNoTracking().SingleAsync(x => x.Id == bookId);
    }
}
