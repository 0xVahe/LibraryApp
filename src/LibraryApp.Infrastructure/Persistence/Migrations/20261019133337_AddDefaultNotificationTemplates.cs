using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The default template of every notification type (NOT-02), so every environment can send notifications; until
    /// now only the Development seed created them.
    /// <para>
    /// Hand-written, idempotent, the same rule as the Development seeder: a type that already has any template (active or
    /// not, possibly edited by an administrator, NOT-09) is left alone; only missing types get their default. Ids follow
    /// the seed pattern 40000000-0000-0000-0001-&lt;type&gt;, so Development databases seeded earlier are skipped.
    /// Down removes only defaults still unchanged, never a template an administrator edited.
    /// </para>
    /// <para>
    /// The templates are also <c>HasData</c> of <c>NotificationTemplateConfiguration</c>, so the model snapshot knows
    /// them. The generated InsertData was replaced by this idempotent SQL: every Development database seeded before
    /// already has these ids, and a plain insert would fail there with a primary key violation.
    /// </para>
    /// </summary>
    public partial class AddDefaultNotificationTemplates : Migration
    {
        // Values frozen with the migration: the seed data may change later, this migration must not.
        private static readonly (int Type, string Name, string Subject, string Body)[] Templates =
        [
            (1, "Welcome", "Welcome to the Ameriabank library",
                "<p>Dear {{EmployeeName}},</p><p>You are now registered as a reader of the corporate library. You can search the catalogue, borrow books and reserve titles that are currently on loan.</p>"),
            (2, "LoanConfirmation", "Your loan of \"{{BookTitle}}\"",
                "<p>Dear {{EmployeeName}},</p><p>You have borrowed \"{{BookTitle}}\". Please return it by {{DueDate}}.</p>"),
            (3, "DueSoonReminder", "Reminder: \"{{BookTitle}}\" is due soon",
                "<p>Dear {{EmployeeName}},</p><p>\"{{BookTitle}}\" is due on {{DueDate}}. Please return or renew it in time.</p>"),
            (4, "OverdueNotice", "Overdue: \"{{BookTitle}}\"",
                "<p>Dear {{EmployeeName}},</p><p>\"{{BookTitle}}\" was due on {{DueDate}} and is now overdue. Please return it as soon as possible.</p>"),
            (5, "ReservationReady", "Your reserved book \"{{BookTitle}}\" is ready",
                "<p>Dear {{EmployeeName}},</p><p>\"{{BookTitle}}\" is ready for pickup. Please collect it by {{ReadyUntil}}.</p>"),
            (6, "PenaltyApplied", "Library penalty points added",
                "<p>Dear {{EmployeeName}},</p><p>{{Points}} penalty point(s) were added to your account. Reason: {{Reason}}. Your active total is now {{TotalPoints}}.</p>"),
            (7, "AccessRestricted", "Your library access is restricted",
                "<p>Dear {{EmployeeName}},</p><p>Your library access has been restricted. Reason: {{Reason}}.</p>"),
            (8, "AccessSuspended", "Your library access is suspended",
                "<p>Dear {{EmployeeName}},</p><p>Your library access has been suspended until {{EffectiveUntil}}. Reason: {{Reason}}.</p>"),
            (9, "DueDateCalendarItem", "Return \"{{BookTitle}}\" to the library",
                "<p>Dear {{EmployeeName}},</p><p>Calendar reminder: return \"{{BookTitle}}\" to the library on {{DueDate}}.</p>"),
            (10, "ReturnReceipt", "Return receipt for \"{{BookTitle}}\"",
                "<p>Dear {{EmployeeName}},</p><p>Thank you. \"{{BookTitle}}\" was returned on {{ReturnedDate}}.</p>"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (type, _, subject, body) in Templates)
                migrationBuilder.Sql(
                    $"IF NOT EXISTS (SELECT 1 FROM [NotificationTemplates] WHERE [Type] = {type}) " +
                    "INSERT INTO [NotificationTemplates] ([Id], [Type], [Subject], [BodyTemplate], [IsActive]) " +
                    $"VALUES ('{IdOf(type)}', {type}, N'{Quote(subject)}', N'{Quote(body)}', 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (type, _, subject, body) in Templates)
                migrationBuilder.Sql(
                    $"DELETE FROM [NotificationTemplates] WHERE [Id] = '{IdOf(type)}' " +
                    $"AND [Subject] = N'{Quote(subject)}' AND [BodyTemplate] = N'{Quote(body)}';");
        }

        private static string IdOf(int type) => $"40000000-0000-0000-0001-{type:D12}";

        private static string Quote(string text) => text.Replace("'", "''");
    }
}
