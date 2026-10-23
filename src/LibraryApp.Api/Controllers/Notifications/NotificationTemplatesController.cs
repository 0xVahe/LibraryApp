using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Notifications;

/// <summary>
/// Administration of the notification templates (NOT-09): the Library Administrator lists, creates, edits, activates and
/// deletes templates, and previews a text before saving it. A template may use only the placeholders available for its
/// type, and the active template of a type cannot be deleted.
/// </summary>
[ApiController]
[Tags("Notification templates")]
[Route("api/notification-templates")]
[Authorize(Roles = StaffRoles.LibraryAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class NotificationTemplatesController(INotificationTemplateService templates) : ControllerBase
{
    /// <summary>Lists the templates (NOT-09).</summary>
    /// <remarks>Ordered by notification type, the active template first. Each template shows the placeholders it may use.</remarks>
    /// <param name="type">Only the templates of this notification type; omit for all.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NotificationTemplateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<NotificationTemplateDto>>> List([FromQuery] NotificationType? type, CancellationToken ct) =>
        Ok(await templates.ListAsync(type, ct));

    /// <summary>Gets a template (NOT-09).</summary>
    /// <remarks>
    /// Returns the subject, the body and the placeholders available for the type of the template, active or not. An unknown
    /// id is 404. Only the Library Administrator can call it.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<NotificationTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationTemplateDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await templates.GetAsync(id, ct));

    /// <summary>Creates a template (NOT-09).</summary>
    /// <remarks>
    /// The new template is inactive: emails keep using the active template until you activate this one. A placeholder that is
    /// not available for the type, or that is not written as {{Name}}, is refused with 400.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<NotificationTemplateDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationTemplateDto>> Create(CreateNotificationTemplateRequest request, CancellationToken ct)
    {
        var template = await templates.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, template);
    }

    /// <summary>Changes the subject and body of a template (NOT-09).</summary>
    /// <remarks>The type does not change. The same placeholder rules as when creating apply.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<NotificationTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationTemplateDto>> Update(Guid id, UpdateNotificationTemplateRequest request, CancellationToken ct) =>
        Ok(await templates.UpdateAsync(id, request, ct));

    /// <summary>Makes a template the active one of its type (NOT-09).</summary>
    /// <remarks>The previously active template of the type is deactivated in the same step. Repeating the call changes nothing.</remarks>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType<NotificationTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NotificationTemplateDto>> Activate(Guid id, CancellationToken ct) =>
        Ok(await templates.ActivateAsync(id, ct));

    /// <summary>Deletes an inactive template (NOT-09).</summary>
    /// <remarks>The active template cannot be deleted (422): activate another template of the type first.</remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await templates.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Previews a template text without saving it (NOT-09).</summary>
    /// <remarks>
    /// Renders the subject and body with example values (for example "Anna Petrosyan", "Clean Code") in the Ameriabank email
    /// layout, the way an employee would receive it. The same placeholder rules as when saving apply.
    /// </remarks>
    [HttpPost("preview")]
    [ProducesResponseType<NotificationTemplatePreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<NotificationTemplatePreviewDto> Preview(PreviewNotificationTemplateRequest request) =>
        Ok(templates.Preview(request));
}
