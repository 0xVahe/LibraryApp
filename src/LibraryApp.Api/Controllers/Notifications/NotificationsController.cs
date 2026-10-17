using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Notifications;

/// <summary>
/// In-app notifications of the signed-in staff member (NOT-04). Every role reads and marks only their own (NFR-03);
/// the owner comes from the access token.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Roles = Callers)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    private const string Callers = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian + "," + StaffRoles.StaffMember;

    /// <summary>My in-app notifications (NOT-04).</summary>
    /// <remarks>Unread first, then newest first. Only the caller's own notifications are returned.</remarks>
    /// <param name="page">Page number, starting from 1.</param>
    /// <param name="pageSize">Items per page: default 20, maximum 100.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    [HttpGet("me")]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await notifications.ListAsync(unreadOnly: false, new PageRequest(page, pageSize), ct));

    /// <summary>My unread in-app notifications (NOT-04), newest first.</summary>
    /// <param name="page">Page number, starting from 1.</param>
    /// <param name="pageSize">Items per page: default 20, maximum 100.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    [HttpGet("me/unread")]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> ListUnread(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await notifications.ListAsync(unreadOnly: true, new PageRequest(page, pageSize), ct));

    /// <summary>Marks one of my notifications as read (NOT-04).</summary>
    /// <remarks>
    /// Repeating the call is safe and keeps the first read time. A notification of another person, or an unknown id, is
    /// 404; an Outlook (e-mail) notification cannot be marked (422).
    /// </remarks>
    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType<NotificationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<NotificationDto>> MarkRead(Guid id, CancellationToken ct) =>
        Ok(await notifications.MarkReadAsync(id, ct));
}
