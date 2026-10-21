using System.Globalization;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace LibraryApp.Api.Areas.Notifications.Pages;

/// <summary>
/// NOT-04: the caller's in-app notifications, unread first, with "Mark as read". Every role sees only their own (NFR-03):
/// the service takes the staff member from the signed-in user.
/// </summary>
public sealed class IndexModel(INotificationService notifications, IClock clock) : PageModel
{
    public const string UnreadPath = UiNavigation.Notifications + "?unread=true";

    [BindProperty(SupportsGet = true)]
    public bool Unread { get; set; }

    public PagedResult<NotificationDto> Result { get; private set; } = new([], 0, 1, PageRequest.DefaultPageSize);

    public async Task<IActionResult> OnGetAsync([FromQuery(Name = PagerModel.PageParameter)] int page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Result = await notifications.ListAsync(Unread, new PageRequest(page), ct);

        // A page past the end (e.g. after marking the only item of the last page) goes to the last page with items.
        if (Result.Items.Count == 0 && Result.Page > 1 && Result.TotalPages > 0)
            return LocalRedirect(PathOf(Unread, Result.TotalPages));

        return Page();
    }

    public async Task<IActionResult> OnPostReadAsync(Guid id, bool unread, int page, CancellationToken ct)
    {
        try
        {
            await notifications.MarkReadAsync(id, ct);
        }
        catch (DomainException ex)
        {
            return this.DomainError(ex);
        }

        return LocalRedirect(PathOf(unread, page));
    }

    private static string PathOf(bool unread, int page)
    {
        string path = unread ? UnreadPath : UiNavigation.Notifications;
        return page > 1 ? QueryHelpers.AddQueryString(path, PagerModel.PageParameter, page.ToString(CultureInfo.InvariantCulture)) : path;
    }

    public string LocalTime(DateTime utc) => clock.ToLocal(utc).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}
