using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace LibraryApp.Api.Areas.Catalog.Pages;

/// <summary>
/// Review book suggestions (CAT-10), Library Administrator only: every suggestion with a status filter; a Pending one
/// is approved, or rejected with a required comment. Approving creates no book: a librarian adds it with Add book.
/// </summary>
[Authorize(Policy = UiPolicies.Administrator)]
public sealed class SuggestionReviewModel(IBookSuggestionService suggestions, IClock clock) : PageModel
{
    public const string Path = "/catalog/suggestions/review";

    /// <summary>The status filter (<c>?status=Pending</c>); null shows every suggestion.</summary>
    [BindProperty(SupportsGet = true)]
    public BookSuggestionStatus? Status { get; set; }

    public PagedResult<BookSuggestionDto> Result { get; private set; } = PagedResult<BookSuggestionDto>.Empty(new PageRequest());

    /// <summary>The suggestion whose Reject form came back with an error; its comment field shows the error.</summary>
    public Guid? RejectId { get; private set; }

    /// <summary>The comment of that Reject form, so the administrator does not type it again.</summary>
    public string? RejectComment { get; private set; }

    public static IReadOnlyList<BookSuggestionStatus> Statuses { get; } = Enum.GetValues<BookSuggestionStatus>();

    /// <summary>A domain error of the last post (for example, the suggestion was already reviewed), shown above the list.</summary>
    public string? FormError =>
        ModelState.TryGetValue(string.Empty, out var entry) && entry.Errors.Count > 0
            ? string.Join(" ", entry.Errors.Select(error => error.ErrorMessage))
            : null;

    /// <summary>The validation message of the rejection comment, or null.</summary>
    public string? CommentError =>
        ModelState.TryGetValue(nameof(RejectBookSuggestionRequest.Comment), out var entry) && entry.Errors.Count > 0
            ? string.Join(" ", entry.Errors.Select(error => error.ErrorMessage))
            : null;

    public string LocalDate(DateTime utc) => clock.ToLocalDate(utc).ToString("yyyy-MM-dd");

    /// <summary>The list with this status filter (the page is reset to 1).</summary>
    public static string FilterHref(BookSuggestionStatus? status) =>
        status is { } value ? QueryHelpers.AddQueryString(Path, "status", value.ToString()) : Path;

    /// <summary>The URL a review form posts to: the handler, and the filter and page of the list, to come back to it.</summary>
    public string HandlerHref(string handler) => QueryHelpers.AddQueryString(ListHref(), "handler", handler);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostApproveAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await suggestions.ApproveAsync(id, ct);
        }
        catch (DomainException ex)
        {
            await LoadAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, "Suggestion approved.", "A librarian can now add the book with Add book on the Catalog page.");
        return Redirect(ListHref());
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, RejectBookSuggestionRequest input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            RejectId = id;
            RejectComment = input.Comment;
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await suggestions.RejectAsync(id, input, ct);
        }
        catch (DomainException ex)
        {
            await LoadAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, "Suggestion rejected.", "The staff member sees your comment in their list.");
        return Redirect(ListHref());
    }

    /// <summary>The list as it is shown now: the status filter and the page number.</summary>
    private string ListHref()
    {
        var href = FilterHref(Status);
        return PageNumber() > 1 ? QueryHelpers.AddQueryString(href, PagerModel.PageParameter, PageNumber().ToString()) : href;
    }

    // "page" is a route value of Razor Pages, so the page number is read from the query string, not bound.
    private int PageNumber() => int.TryParse(Request.Query[PagerModel.PageParameter], out var page) && page > 1 ? page : 1;

    private async Task LoadAsync(CancellationToken ct) =>
        Result = await suggestions.ListAsync(new BookSuggestionListRequest(Status, PageNumber()), ct);
}
