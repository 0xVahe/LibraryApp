using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>The Book page (CAT-06, CAT-08) with the real host and the seeded catalog.</summary>
public class BookPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly Guid SeededBook = CatalogSeedIds.Book(1);

    [Fact]
    public async Task The_page_opens_for_a_staff_member_without_Edit()
    {
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        using var response = await client.GetAsync(BookPath(SeededBook));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var book = await DetailsAsync(SeededBook);
        Assert.Contains($"<h1>{book.Title}</h1>", html);
        Assert.Contains($"<dd>{book.Isbn}</dd>", html);
        Assert.DoesNotContain($"href=\"/catalog/books/{SeededBook}/edit\"", html);
        Assert.Contains("Get this book", html);   // the Borrowing group's ReservePanel
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Edit_is_shown_to_librarians_and_library_administrators(bool administrator)
    {
        using var client = await SignedInAsync(administrator
            ? StaffSeedIds.StaffMembers.LilitGrigoryan
            : StaffSeedIds.StaffMembers.AramSargsyan);

        var html = await client.GetStringAsync(BookPath(SeededBook));

        Assert.Contains($"href=\"/catalog/books/{SeededBook}/edit\"", html);
    }

    [Fact]
    public async Task An_unknown_or_deleted_book_shows_Not_found()
    {
        var deleted = await CreateBookAsync("Deleted From The Page");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IBookService>().DeleteAsync(deleted);
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        using var unknown = await client.GetAsync(BookPath(Guid.NewGuid()));
        using var gone = await client.GetAsync(BookPath(deleted));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Availability_and_the_return_date_match_GET_api_books_id()
    {
        var bookId = await BookWithAnOpenLoanAsync();
        using var api = ApiClient();
        var answer = (await api.GetFromJsonAsync<BookDetailsDto>($"/api/books/{bookId}"))!;
        Assert.NotNull(answer.NextExpectedReturnDate);
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        var html = await client.GetStringAsync(BookPath(bookId));

        Assert.Contains($"<span class=\"stat-value\" id=\"availability\">{answer.Availability}</span>", html);
        var returnDate = answer.NextExpectedReturnDate!.Value;
        Assert.Contains($"<time id=\"next-return\" datetime=\"{returnDate:yyyy-MM-dd}\">"
            + returnDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + "</time>", html);
    }

    [Fact]
    public async Task A_book_without_a_loan_has_no_return_date()
    {
        var bookId = await CreateBookAsync("Never Lent");
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        var html = await client.GetStringAsync(BookPath(bookId));

        Assert.Contains("<span class=\"stat-value\" id=\"availability\">0 of 0 available</span>", html);
        Assert.DoesNotContain("id=\"next-return\"", html);
    }

    [Fact]
    public async Task Lost_and_Withdrawn_copies_are_not_in_the_table()
    {
        var bookId = await CreateBookAsync("Copies In Every Status");
        var prefix = "BP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using (var scope = factory.Services.CreateScope())
        {
            var copies = scope.ServiceProvider.GetRequiredService<IBookCopyService>();
            foreach (var (suffix, status) in new[] { ("A", (BookCopyStatus?)null), ("L", BookCopyStatus.Lost), ("W", BookCopyStatus.Withdrawn), ("R", BookCopyStatus.UnderRepair) })
            {
                var copy = await copies.AddCopyAsync(bookId, new AddCopyRequest($"{prefix}-{suffix}", BookCopyCondition.Good, "S1-01"));
                if (status is { } next)
                    await copies.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(next));
            }
        }
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        var html = await client.GetStringAsync(BookPath(bookId));

        Assert.Contains($"<td data-label=\"Barcode\">{prefix}-A</td>", html);
        Assert.Contains($"<td data-label=\"Barcode\">{prefix}-R</td>", html);
        Assert.DoesNotContain($"{prefix}-L", html);
        Assert.DoesNotContain($"{prefix}-W", html);
    }

    [Fact]
    public async Task The_category_links_to_the_catalog_with_the_parent_first_for_a_subcategory()
    {
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        var inSubcategory = CategoryOf(await client.GetStringAsync(BookPath(SeededBook)));
        var inTopLevel = CategoryOf(await client.GetStringAsync(BookPath(CatalogSeedIds.Book(2))));

        Assert.Matches($"^\\s*{Link(CatalogSeedIds.Category(1), "Banking &amp; Finance")}\\s*/\\s*{Link(CatalogSeedIds.Category(9), "Investing &amp; Markets")}\\s*$", inSubcategory);
        Assert.Matches($"^\\s*{Link(CatalogSeedIds.Category(1), "Banking &amp; Finance")}\\s*$", inTopLevel);

        static string CategoryOf(string html) =>
            Regex.Match(html, "<dt>Category</dt>\\s*<dd>(.*?)</dd>", RegexOptions.Singleline).Groups[1].Value;
        static string Link(Guid categoryId, string name) => Regex.Escape($"<a href=\"/catalog?categoryId={categoryId}\">{name}</a>");
    }

    [Fact]
    public async Task More_in_the_category_shows_up_to_three_other_books_and_never_this_one()
    {
        var book = await DetailsAsync(SeededBook);
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        var html = await client.GetStringAsync(BookPath(SeededBook));

        Assert.Contains($"More in {book.CategoryName}", WebUtility.HtmlDecode(html));
        var list = Regex.Match(html, "<ul class=\"list\" id=\"more-books\">.*?</ul>", RegexOptions.Singleline).Value;
        var links = Regex.Matches(list, "href=\"/catalog/books/([0-9a-f-]{36})\"").Select(match => Guid.Parse(match.Groups[1].Value)).ToList();
        Assert.InRange(links.Count, 1, 3);
        Assert.DoesNotContain(SeededBook, links);
    }

    [Fact]
    public async Task A_book_with_a_cover_renders_the_image_over_the_text_cover()
    {
        var book = await DetailsAsync(SeededBook);
        Assert.NotNull(book.CoverImageUrl);
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        var html = await client.GetStringAsync(BookPath(SeededBook));

        var cover = HeroCover(html);
        Assert.Contains($"<img class=\"cover-img\" src=\"{book.CoverImageUrl}\" alt=\"\" loading=\"lazy\" referrerpolicy=\"no-referrer\">", cover);
        Assert.Contains("has-image", cover);
        Assert.Contains("<span>", cover);   // the text cover stays underneath
    }

    [Fact]
    public async Task A_book_without_a_cover_renders_only_the_text_cover()
    {
        var bookId = await CreateBookAsync("No Cover Here");
        using var client = await SignedInAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);

        var html = await client.GetStringAsync(BookPath(bookId));

        var cover = HeroCover(html);
        Assert.DoesNotContain("<img", cover);
        Assert.DoesNotContain("has-image", cover);
        Assert.Contains("<span>No Cover Here</span>", cover);
    }

    private Task<HttpClient> SignedInAsync(Guid staffId) => SignedInAsync(factory, staffId);

    internal static Task<HttpClient> SignedInAsync(ApiHostFactory host, Guid staffId) => UiTestClient.SignedInAsync(host,
        StaffSeedData.People.Single(person => person.StaffMemberId == staffId).Email, StaffSeedData.Password);

    internal static string BookPath(Guid id) => $"/catalog/books/{id}";

    /// <summary>The big cover next to the details.</summary>
    internal static string HeroCover(string html) =>
        Regex.Match(html, "<div class=\"grid book-hero\">\\s*(<div class=\"cover[^\"]*\" aria-hidden=\"true\">.*?</div>)", RegexOptions.Singleline).Groups[1].Value;

    private HttpClient ApiClient()
    {
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
            factory.Account(StaffRoles.StaffMember)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<BookDetailsDto> DetailsAsync(Guid bookId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBookDetailsService>().GetBookDetailsAsync(bookId);
    }

    /// <summary>A seeded book with an Active or Overdue loan, so it has a next expected return date.</summary>
    private async Task<Guid> BookWithAnOpenLoanAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        return await db.Loans
            .Where(loan => loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
            .Join(db.BookCopies, loan => loan.BookCopyId, copy => copy.Id, (loan, copy) => copy.BookId)
            .FirstAsync();
    }

    /// <summary>A new book without copies and without a cover, in a seeded category, by a seeded author.</summary>
    private async Task<Guid> CreateBookAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBookService>().CreateAsync(new CreateBookRequest(
            title, NewIsbn(), 2020, "en", null, CatalogSeedIds.Category(8), null, [CatalogSeedData.Authors[0].Id]));
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

/// <summary>With covers off (and so also when the storage cannot be reached), the Book page shows the text cover.</summary>
public class BookPageWithoutCoversTests(NoCoversHostFactory factory) : IClassFixture<NoCoversHostFactory>
{
    [Fact]
    public async Task Covers_off_shows_the_text_cover_and_no_image()
    {
        var bookId = CatalogSeedIds.Book(1);
        using var client = await BookPageTests.SignedInAsync(factory, StaffSeedIds.StaffMembers.AnnaPetrosyan);

        var html = await client.GetStringAsync(BookPageTests.BookPath(bookId));

        var cover = BookPageTests.HeroCover(html);
        Assert.NotEmpty(cover);
        Assert.DoesNotContain("<img", cover);
        Assert.Contains("<span>", cover);
    }
}
