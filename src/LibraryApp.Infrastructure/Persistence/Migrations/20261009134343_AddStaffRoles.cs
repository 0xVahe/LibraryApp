using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The three account roles (StaffRoleIds, HasData in IdentityConfiguration), so every environment has them.
    /// <para>
    /// Hand-edited, idempotent: Development databases already have these rows (same ids) from the Staff seed, and the
    /// generated InsertData would stop the migration there with a primary-key violation. A role is inserted only when
    /// neither its id nor its name exists. Down removes only roles that no account holds, so rolling back never
    /// takes roles away from users (role links cascade with the role).
    /// </para>
    /// </summary>
    public partial class AddStaffRoles : Migration
    {
        // Values frozen with the migration; StaffRoleIds may change later, this migration must not.
        private static readonly (string Id, string Name, string NormalizedName)[] Roles =
        [
            ("20000000-0000-0000-0005-000000000001", "LibraryAdministrator", "LIBRARYADMINISTRATOR"),
            ("20000000-0000-0000-0005-000000000002", "Librarian", "LIBRARIAN"),
            ("20000000-0000-0000-0005-000000000003", "StaffMember", "STAFFMEMBER"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (id, name, normalizedName) in Roles)
                migrationBuilder.Sql(
                    $"IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Id] = '{id}' OR [NormalizedName] = N'{normalizedName}') " +
                    $"INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES ('{id}', N'{name}', N'{normalizedName}', N'{id}');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (id, _, _) in Roles)
                migrationBuilder.Sql(
                    $"DELETE FROM [AspNetRoles] WHERE [Id] = '{id}' " +
                    $"AND NOT EXISTS (SELECT 1 FROM [AspNetUserRoles] WHERE [RoleId] = '{id}');");
        }
    }
}
