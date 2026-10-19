using LibraryApp.Domain.Notifications;

namespace LibraryApp.Infrastructure.Persistence.Seed.Notifications;

/// <summary>
/// Default template of every notification type of the catalogue (requirements section 9). Constant ids, pattern
/// 40000000-0000-0000-0001-&lt;NotificationType value&gt; (4 = Notifications module); they never change.
/// The only source of the default templates: <c>NotificationTemplateConfiguration</c> passes them to <c>HasData</c>, so
/// they reach every database through the migrations (no seeder, no environment condition).
/// </summary>
/// <remarks>
/// Only {{EmployeeName}}, {{BookTitle}} and {{DueDate}} are named by NOT-02. All other placeholders
/// ({{ReadyUntil}}, {{Points}}, {{TotalPoints}}, {{Reason}}, {{EffectiveUntil}}, {{ReturnedDate}}) are PROVISIONAL, taken
/// from the fields of the domain events, and must be aligned with the renderer once it exists. Bodies are plain HTML
/// fragments in English (NFR-12); the Ameriabank layout wrapper (NOT-02) belongs to the renderer.
/// </remarks>
public static class NotificationTemplateSeedData
{
    public sealed record SeedTemplate(NotificationType Type, string Subject, string Body)
    {
        public Guid Id => IdOf(Type);
    }

    public static Guid IdOf(NotificationType type) =>
        new($"40000000-0000-0000-0001-{(int)type:D12}");

    public static IReadOnlyList<SeedTemplate> Templates { get; } =
    [
        new(NotificationType.Welcome,
            "Welcome to the Ameriabank library",
            "<p>Dear {{EmployeeName}},</p><p>You are now registered as a reader of the corporate library. " +
            "You can search the catalogue, borrow books and reserve titles that are currently on loan.</p>"),

        new(NotificationType.LoanConfirmation,
            "Your loan of \"{{BookTitle}}\"",
            "<p>Dear {{EmployeeName}},</p><p>You have borrowed \"{{BookTitle}}\". Please return it by {{DueDate}}.</p>"),

        new(NotificationType.DueSoonReminder,
            "Reminder: \"{{BookTitle}}\" is due soon",
            "<p>Dear {{EmployeeName}},</p><p>\"{{BookTitle}}\" is due on {{DueDate}}. " +
            "Please return or renew it in time.</p>"),

        new(NotificationType.OverdueNotice,
            "Overdue: \"{{BookTitle}}\"",
            "<p>Dear {{EmployeeName}},</p><p>\"{{BookTitle}}\" was due on {{DueDate}} and is now overdue. " +
            "Please return it as soon as possible.</p>"),

        new(NotificationType.ReservationReady,
            "Your reserved book \"{{BookTitle}}\" is ready",
            "<p>Dear {{EmployeeName}},</p><p>\"{{BookTitle}}\" is ready for pickup. " +
            "Please collect it by {{ReadyUntil}}.</p>"),

        new(NotificationType.PenaltyApplied,
            "Library penalty points added",
            "<p>Dear {{EmployeeName}},</p><p>{{Points}} penalty point(s) were added to your account. " +
            "Reason: {{Reason}}. Your active total is now {{TotalPoints}}.</p>"),

        new(NotificationType.AccessRestricted,
            "Your library access is restricted",
            "<p>Dear {{EmployeeName}},</p><p>Your library access has been restricted. Reason: {{Reason}}.</p>"),

        new(NotificationType.AccessSuspended,
            "Your library access is suspended",
            "<p>Dear {{EmployeeName}},</p><p>Your library access has been suspended until {{EffectiveUntil}}. " +
            "Reason: {{Reason}}.</p>"),

        new(NotificationType.DueDateCalendarItem,
            "Return \"{{BookTitle}}\" to the library",
            "<p>Dear {{EmployeeName}},</p><p>Calendar reminder: return \"{{BookTitle}}\" to the library on {{DueDate}}.</p>"),

        new(NotificationType.ReturnReceipt,
            "Return receipt for \"{{BookTitle}}\"",
            "<p>Dear {{EmployeeName}},</p><p>Thank you. \"{{BookTitle}}\" was returned on {{ReturnedDate}}.</p>"),
    ];
}
