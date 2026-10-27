using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.Areas.Catalog;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-05: the Catalog page is available to every signed-in role and keeps its search experience server-side.</summary>
public sealed class CatalogPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string StaffMember = "anna.petrosyan@ameriabank.am";
    private const string Librarian = "aram.sargsyan@ameriabank.am";
    private const string Administrator = "lilit.grigoryan@ameriabank.am";

    [Fact]
    public async Task A_staff_member_can_open_catalog_and_search_returns_expected_book()
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        var page = await client.GetAsync("/catalog?search=Risk&language=en&available=true&page=1");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Catalog", html);
        Assert.Contains("Risk Management and Financial Institutions", html);
        Assert.Contains("name=\"search\"", html);
        Assert.Contains("name=\"language\"", html);
        Assert.Contains("name=\"available\"", html);
        Assert.DoesNotContain("Add book", html);
        Assert.DoesNotContain("Import CSV", html);
        Assert.DoesNotContain("href=\"/catalog/import\"", html);
        Assert.Contains("href=\"/catalog/suggestions\"", html);
        Assert.DoesNotContain("href=\"/catalog/categories\"", html);
        Assert.DoesNotContain("href=\"/catalog/authors\"", html);
        Assert.DoesNotContain("href=\"/catalog/publishers\"", html);
    }

    [Theory]
    [InlineData(Librarian)]
    [InlineData(Administrator)]
    public async Task Library_staff_see_the_add_book_and_import_actions(string email)
    {
        using var client = await UiTestClient.SignedInAsync(factory, email, StaffSeedData.Password);

        var html = await client.GetStringAsync("/catalog");

        Assert.Contains("href=\"/catalog/books/new\"", html);
        Assert.Contains("Add book", html);
        Assert.Contains("href=\"/catalog/import\"", html);
        Assert.Contains("Import CSV", html);
        Assert.Contains("href=\"/catalog/suggestions\"", html);
    }

    [Theory]
    [InlineData(Librarian)]
    [InlineData(Administrator)]
    public async Task Library_staff_see_links_to_the_categories_authors_and_publishers_pages(string email)
    {
        using var client = await UiTestClient.SignedInAsync(factory, email, StaffSeedData.Password);

        var html = await client.GetStringAsync("/catalog");

        Assert.Contains("href=\"/catalog/categories\"", html);
        Assert.Contains("href=\"/catalog/authors\"", html);
        Assert.Contains("href=\"/catalog/publishers\"", html);
    }

    [Fact]
    public async Task Paging_keeps_the_language_and_availability_filters()
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        var html = await client.GetStringAsync("/catalog?language=en&available=true&page=2");

        Assert.Contains("class=\"pager\"", html);
        Assert.Contains("language=en", html);
        Assert.Contains("available=true", html);
        Assert.Contains("page=1", html);
    }

    [Fact]
    public async Task Paging_changes_the_books_on_the_page()
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        var firstPage = await client.GetStringAsync("/catalog?page=1");
        var secondPage = await client.GetStringAsync("/catalog?page=2");

        string FirstTitle(string html) => Regex.Match(html, "<span class=\"title\">(?<title>[^<]+)</span>").Groups["title"].Value;

        Assert.NotEqual(FirstTitle(firstPage), FirstTitle(secondPage));
    }

    [Fact]
    public async Task Search_form_preserves_the_selected_category()
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        var categoryId = CatalogSeedIds.Category(7);
        var html = await client.GetStringAsync($"/catalog?categoryId={categoryId}&search=Hug&language=en");

        Assert.Contains($"name=\"categoryId\" value=\"{categoryId}\"", html);
        Assert.Contains("Hug Your Haters", html);
    }

    [Fact]
    public async Task A_seeded_book_with_a_cover_renders_the_required_image_attributes()
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        var html = await client.GetStringAsync("/catalog?search=The+Intelligent+Investor");

        // The colour follows the category, as on the Book page.
        var categoryId = CatalogSeedIds.Category(CatalogSeedData.Books.Single(book => book.Number == 1).CategoryNumber);
        var cover = new BookCoverModel("The Intelligent Investor", null, "cover", categoryId);
        Assert.Contains($"class=\"{cover.CssClass}\"", html);
        Assert.Contains("class=\"cover-img\"", html);
        Assert.Contains("src=\"https://res.cloudinary.com/hil9ktuv/image/upload/library-covers/01.png\"", html);
        Assert.Contains("alt=\"\"", html);
        Assert.Contains("loading=\"lazy\"", html);
        Assert.Contains("referrerpolicy=\"no-referrer\"", html);
    }

    [Fact]
    public async Task A_page_past_the_last_one_says_so_and_links_the_first_page_with_the_filters()
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        var html = await client.GetStringAsync("/catalog?language=en&page=999");

        Assert.Contains("This page is empty.", html);
        Assert.Contains("<a href=\"/catalog?language=en\">Go to the first page</a>", html);
        Assert.DoesNotContain("No books match your filters.", html);
        Assert.DoesNotContain("Showing 0", html);
    }

    [Fact]
    public async Task A_category_with_subcategories_shows_them_in_a_second_row_of_chips()
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);
        var banking = CatalogSeedIds.Category(1);

        var html = await client.GetStringAsync($"/catalog?language=en&categoryId={banking}");

        var subcategories = SubcategoryRow(html, "Banking &amp; Finance");
        Assert.Contains($"<a class=\"chip chip-subcategory\" href=\"/catalog?language=en&amp;categoryId={banking}\" aria-current=\"true\">All in Banking &amp; Finance</a>", subcategories);
        Assert.Contains($"<a class=\"chip chip-subcategory\" href=\"/catalog?language=en&amp;categoryId={CatalogSeedIds.Category(9)}\">Investing &amp; Markets</a>", subcategories);
        Assert.Contains($"<a class=\"chip chip-subcategory\" href=\"/catalog?language=en&amp;categoryId={CatalogSeedIds.Category(10)}\">Banking &amp; Financial History</a>", subcategories);
    }

    [Fact]
    public async Task A_selected_subcategory_keeps_its_parent_current_and_lists_only_its_books()
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);
        var banking = CatalogSeedIds.Category(1);
        var investing = CatalogSeedIds.Category(9);

        var html = await client.GetStringAsync($"/catalog?categoryId={investing}");

        var categories = Regex.Match(html, "<nav class=\"chips\" aria-label=\"Categories\">.*?</nav>", RegexOptions.Singleline).Value;
        Assert.Contains($"href=\"/catalog?categoryId={banking}\" aria-current=\"true\">Banking &amp; Finance</a>", categories);
        Assert.Contains($"href=\"/catalog?categoryId={investing}\" aria-current=\"true\">Investing &amp; Markets</a>",
            SubcategoryRow(html, "Banking &amp; Finance"));
        Assert.Contains("The Big Short", html);
        Assert.DoesNotContain("The Ascent of Money", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?categoryId=10000000-0000-0000-0001-000000000007")]
    public async Task Without_a_category_or_with_one_that_has_no_subcategories_there_is_no_second_row(string query)
    {
        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);

        var html = await client.GetStringAsync("/catalog" + query);

        Assert.DoesNotContain("aria-label=\"Subcategories of", html);
    }

    [Fact]
    public async Task A_book_without_a_cover_renders_only_the_text_cover()
    {
        var title = "Catalog page no-cover " + Guid.NewGuid().ToString("N")[..8];
        var categoryId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var category = new Category(categoryId, title + " category");
            var author = new Author(Guid.NewGuid(), title + " author");
            var book = new Book(Guid.NewGuid(), title, "9791000000015", 2024, "en", null, category.Id, null);
            db.AddRange(category, author, book, new BookAuthor(book.Id, author.Id));
            await db.SaveChangesAsync();
        }

        using var client = await UiTestClient.SignedInAsync(factory, StaffMember, StaffSeedData.Password);
        var html = await client.GetStringAsync($"/catalog?search={Uri.EscapeDataString(title)}");

        Assert.Contains(title, html);
        Assert.DoesNotContain("class=\"cover-img\"", html);
        Assert.Contains($"class=\"{new BookCoverModel(title, null, null, categoryId).CssClass}\"", html);
    }

    private static string SubcategoryRow(string html, string encodedParentName) =>
        Regex.Match(html, $"<nav class=\"chips chips-subcategories\" aria-label=\"Subcategories of {Regex.Escape(encodedParentName)}\">.*?</nav>",
            RegexOptions.Singleline).Value;
}
