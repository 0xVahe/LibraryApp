using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace LibraryApp.Api.Areas.Catalog.Pages;

/// <summary>The Catalog page for every signed-in role (CAT-05).</summary>
[Authorize(Policy = UiPolicies.SignedIn)]
public sealed class IndexModel(
    IBookSearchService searchService,
    ICategoryService categories) : PageModel
{
    public const int PageSize = PageRequest.DefaultPageSize;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Language { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Available { get; set; }

    public PagedResult<BookSearchResultDto> Result { get; private set; } =
        PagedResult<BookSearchResultDto>.Empty(new PageRequest());

    public CategoryTree CategoryTree { get; private set; } = new([]);

    public IReadOnlyList<CatalogLanguage> Languages => CatalogLanguages.All;

    public bool CanManageBooks => User.IsInRole(StaffRoles.Librarian)
        || User.IsInRole(StaffRoles.LibraryAdministrator);

    public async Task OnGetAsync(CancellationToken ct)
    {
        CategoryTree = new CategoryTree(await categories.ListAsync(ct));
        int pageNumber = int.TryParse(Request.Query[PagerModel.PageParameter], out var requestedPage)
            ? requestedPage
            : 1;

        Result = await searchService.SearchAsync(
            new BookSearchRequest(Search, CategoryId, Language, Available ? true : null,
                new PageRequest(pageNumber, PageSize)), ct);
    }

    public string CategoryHref(Guid? categoryId)
    {
        var values = new List<KeyValuePair<string, string?>>();
        if (!string.IsNullOrWhiteSpace(Search))
            values.Add(new("search", Search));
        if (!string.IsNullOrWhiteSpace(Language))
            values.Add(new("language", Language));
        if (Available)
            values.Add(new("available", "true"));
        if (categoryId is { } id)
            values.Add(new("categoryId", id.ToString()));
        return QueryHelpers.AddQueryString("/catalog", values);
    }
}
