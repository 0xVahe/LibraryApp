using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Notifications.Components;

/// <summary>
/// The latest in-app notifications of the signed-in user on My library (NOT-04): <c>&lt;vc:latest-notifications /&gt;</c>.
/// Unread first, then the newest; the full list is the Notifications page. Always the caller's own (NFR-03).
/// </summary>
public sealed class LatestNotificationsViewComponent(INotificationService notifications) : ViewComponent
{
    public const int Count = 5;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var latest = await notifications.ListAsync(unreadOnly: false, new PageRequest(1, Count), HttpContext.RequestAborted);
        return View("~/Areas/Notifications/Components/LatestNotifications/Default.cshtml", latest.Items);
    }
}
