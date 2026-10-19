using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The default template of <c>NotificationType.PasswordChanged</c> (11), the security notice of
    /// <c>StaffPasswordChanged</c> (MEM-02, NOT-02).
    /// <para>
    /// The same rule as <c>AddDefaultNotificationTemplates</c>: hand-written and idempotent. The template is inserted only
    /// when type 11 has no template yet, so a template an administrator added first is kept (NOT-09); <c>Down</c> removes
    /// it only while it is unchanged. The template is also <c>HasData</c> of <c>NotificationTemplateConfiguration</c>, so
    /// the model snapshot knows it; the generated InsertData was replaced by this SQL.
    /// </para>
    /// </summary>
    public partial class AddPasswordChangedTemplate : Migration
    {
        private const int Type = 11;
        private const string Id = "40000000-0000-0000-0001-000000000011";

        // Values frozen with the migration: the seed data may change later, this migration must not.
        private const string Subject = "Your library password was changed";
        private const string Body =
            "<p>Dear {{EmployeeName}},</p><p>The password of your library account was changed. If you made this change, no action is needed.</p><p>If you did not change your password, contact the library administrator immediately.</p>";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"IF NOT EXISTS (SELECT 1 FROM [NotificationTemplates] WHERE [Type] = {Type}) " +
                "INSERT INTO [NotificationTemplates] ([Id], [Type], [Subject], [BodyTemplate], [IsActive]) " +
                $"VALUES ('{Id}', {Type}, N'{Quote(Subject)}', N'{Quote(Body)}', 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"DELETE FROM [NotificationTemplates] WHERE [Id] = '{Id}' " +
                $"AND [Subject] = N'{Quote(Subject)}' AND [BodyTemplate] = N'{Quote(Body)}';");
        }

        private static string Quote(string text) => text.Replace("'", "''");
    }
}
