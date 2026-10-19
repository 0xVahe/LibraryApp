using LibraryApp.Api.Ui;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Catalog.Pages.Books;

/// <summary>
/// The Book page (CAT-06, CAT-08) for every signed-in role: details, cover, "N of M available", the next expected
/// return date, the copies still in the collection, more books of the category, and the Borrowing group's
/// "Get this book" panel. An unknown or deleted book shows Not found (the shell's NotFound handling).
/// </summary>
[Authorize(Policy = UiPolicies.SignedIn)]
public sealed class DetailsModel(
    IBookDetailsService details,
    IBookCopyService copies,
    IBookSearchService search,
    ICategoryService categories) : PageModel
{
    /// <summary>How many other books "More in" shows.</summary>
    public const int MoreCount = 3;

    public BookDetailsDto Book { get; private set; } = null!;

    /// <summary>"Parent / Child" for a subcategory, the name for a top-level category.</summary>
    public string CategoryPath { get; private set; } = "";

    public string LanguageName { get; private set; } = "";

    /// <summary>The copies in the collection: Lost and Withdrawn copies are left out.</summary>
    public IReadOnlyList<BookCopyDto> Copies { get; private set; } = [];

    /// <summary>Up to <see cref="MoreCount"/> other books of the same category, never this book.</summary>
    public IReadOnlyList<BookSearchResultDto> MoreBooks { get; private set; } = [];

    /// <summary>Only librarians and library administrators see "Edit".</summary>
    public bool CanEdit => User.IsInRole(StaffRoles.Librarian) || User.IsInRole(StaffRoles.LibraryAdministrator);

    public BookCoverModel Cover => new(Book.Title, Book.Authors.Count > 0 ? string.Join(", ", Book.Authors) : null,
        Book.CoverImageUrl, Book.CategoryId);

    public async Task OnGetAsync(Guid id, CancellationToken ct)
    {
        Book = await details.GetBookDetailsAsync(id, ct);   // unknown or deleted: BookNotFound → the Not found page

        Copies = (await copies.GetCopiesAsync(id, ct))
            .Where(copy => copy.Status is not (BookCopyStatus.Lost or BookCopyStatus.Withdrawn))
            .OrderBy(copy => copy.Barcode, StringComparer.Ordinal)
            .ToList();

        var tree = new CategoryTree(await categories.ListAsync(ct));
        CategoryPath = tree.PathOf(Book.CategoryId) ?? Book.CategoryName;
        LanguageName = CatalogLanguages.NameOf(Book.Language);

        // One more than shown, so the list is still full after this book is left out.
        var sameCategory = await search.SearchAsync(
            new BookSearchRequest(null, Book.CategoryId, null, null, new PageRequest(1, MoreCount + 1)), ct);
        MoreBooks = sameCategory.Items.Where(book => book.Id != id).Take(MoreCount).ToList();
    }
}
