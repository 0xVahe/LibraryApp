using FluentValidation;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Validators;

/// <summary>Shared rules of the template text (NOT-09). The placeholders are checked by the service, which knows the type.</summary>
internal static class NotificationTemplateRules
{
    public static void Subject<T>(IRuleBuilderInitial<T, string?> rule) => rule.Cascade(CascadeMode.Stop)
        .Must(subject => !string.IsNullOrWhiteSpace(subject)).WithMessage("Subject is required.")
        .Must(subject => subject!.Trim().Length <= NotificationTemplate.SubjectMaxLength)
        .WithMessage($"Subject must be {NotificationTemplate.SubjectMaxLength} characters or fewer.");

    public static void Body<T>(IRuleBuilderInitial<T, string?> rule) => rule
        .Must(body => !string.IsNullOrWhiteSpace(body)).WithMessage("BodyTemplate is required.");

    public static void Type<T>(IRuleBuilderInitial<T, NotificationType?> rule) => rule.Cascade(CascadeMode.Stop)
        .NotNull().WithMessage("Type is required.")
        .Must(type => Enum.IsDefined(type!.Value)).WithMessage("Type is not a notification type.");
}

public sealed class CreateNotificationTemplateRequestValidator : AbstractValidator<CreateNotificationTemplateRequest>
{
    public CreateNotificationTemplateRequestValidator()
    {
        NotificationTemplateRules.Type(RuleFor(x => x.Type));
        NotificationTemplateRules.Subject(RuleFor(x => x.Subject));
        NotificationTemplateRules.Body(RuleFor(x => x.BodyTemplate));
    }
}

public sealed class UpdateNotificationTemplateRequestValidator : AbstractValidator<UpdateNotificationTemplateRequest>
{
    public UpdateNotificationTemplateRequestValidator()
    {
        NotificationTemplateRules.Subject(RuleFor(x => x.Subject));
        NotificationTemplateRules.Body(RuleFor(x => x.BodyTemplate));
    }
}

public sealed class PreviewNotificationTemplateRequestValidator : AbstractValidator<PreviewNotificationTemplateRequest>
{
    public PreviewNotificationTemplateRequestValidator()
    {
        NotificationTemplateRules.Type(RuleFor(x => x.Type));
        NotificationTemplateRules.Subject(RuleFor(x => x.Subject));
        NotificationTemplateRules.Body(RuleFor(x => x.BodyTemplate));
    }
}
