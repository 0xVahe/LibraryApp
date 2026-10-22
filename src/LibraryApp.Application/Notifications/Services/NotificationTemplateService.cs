using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Services;

/// <inheritdoc cref="INotificationTemplateService"/>
public sealed class NotificationTemplateService(
    INotificationTemplateRepository templates,
    INotificationTemplateRenderer renderer,
    IUnitOfWork unitOfWork,
    IIdGenerator ids) : INotificationTemplateService
{
    public async Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(NotificationType? type, CancellationToken ct = default) =>
        (await templates.ListAsync(type, ct)).Select(ToDto).ToList();

    public async Task<NotificationTemplateDto> GetAsync(Guid id, CancellationToken ct = default) =>
        ToDto(await FindAsync(id, ct));

    public Task<NotificationTemplateDto> CreateAsync(CreateNotificationTemplateRequest request, CancellationToken ct = default)
    {
        NotificationType type = request.Type!.Value;
        RequireUsablePlaceholders(type, request.Subject!, request.BodyTemplate!);

        return unitOfWork.ExecuteInTransactionAsync(token =>
        {
            // Inactive: the template in use changes only when an administrator activates a new one.
            var template = new NotificationTemplate(ids.NewId(), type, request.Subject!, request.BodyTemplate!, isActive: false);
            templates.Add(template);
            return Task.FromResult(ToDto(template));
        }, ct);
    }

    public Task<NotificationTemplateDto> UpdateAsync(Guid id, UpdateNotificationTemplateRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            NotificationTemplate template = await FindAsync(id, token);
            RequireUsablePlaceholders(template.Type, request.Subject!, request.BodyTemplate!);

            template.Update(request.Subject!, request.BodyTemplate!);
            return ToDto(template);
        }, ct);

    public Task<NotificationTemplateDto> ActivateAsync(Guid id, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            NotificationTemplate template = await FindAsync(id, token);
            if (template.IsActive)
                return ToDto(template);

            // The previous one first: the database allows one active template per type (G4-08).
            await templates.DeactivateOthersAsync(template.Type, template.Id, token);
            template.Activate();
            return ToDto(template);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            NotificationTemplate template = await FindAsync(id, token);
            if (template.IsActive)
                throw new DomainException(NotificationErrors.TemplateActiveCannotBeDeleted);

            templates.Remove(template);
        }, ct);

    public NotificationTemplatePreviewDto Preview(PreviewNotificationTemplateRequest request)
    {
        NotificationType type = request.Type!.Value;
        RequireUsablePlaceholders(type, request.Subject!, request.BodyTemplate!);

        // Same renderer and layout as a real email. Only the placeholders of the type are used, and all have a sample value.
        var values = NotificationPlaceholders.AllowedFor(type).ToDictionary(name => name, name => NotificationPlaceholders.SampleValues[name]);
        RenderedNotification rendered = renderer.Render(request.Subject!, request.BodyTemplate!, values);
        return new NotificationTemplatePreviewDto(rendered.Subject, rendered.HtmlBody);
    }

    /// <summary>
    /// A malformed placeholder would reach the employee as raw braces, and a placeholder without a value for the type would
    /// make every send fail: both are refused when the template is saved.
    /// </summary>
    private static void RequireUsablePlaceholders(NotificationType type, string subject, string body)
    {
        if (!NotificationPlaceholders.IsWellFormed(subject) || !NotificationPlaceholders.IsWellFormed(body))
            throw new DomainException(NotificationErrors.TemplateMalformedPlaceholder);

        IReadOnlyList<string> allowed = NotificationPlaceholders.AllowedFor(type);
        List<string> unknown = NotificationPlaceholders.Find(subject)
            .Concat(NotificationPlaceholders.Find(body))
            .Distinct(StringComparer.Ordinal)
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .ToList();

        if (unknown.Count > 0)
            throw new DomainException(
                NotificationErrors.TemplateUnknownPlaceholder,
                extensions: new Dictionary<string, object?> { ["placeholders"] = unknown, ["allowed"] = allowed });
    }

    private async Task<NotificationTemplate> FindAsync(Guid id, CancellationToken ct) =>
        await templates.GetByIdAsync(id, ct) ?? throw new DomainException(NotificationErrors.TemplateDoesNotExist);

    private static NotificationTemplateDto ToDto(NotificationTemplate template) => new(
        template.Id, template.Type, template.Subject, template.BodyTemplate, template.IsActive,
        NotificationPlaceholders.AllowedFor(template.Type));
}
