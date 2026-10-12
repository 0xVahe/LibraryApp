using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CcEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: -1, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Attempts_NonNegative", "[Attempts] >= 0");
                    table.ForeignKey(
                        name: "FK_Notifications_Loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "Loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BodyTemplate = table.Column<string>(type: "nvarchar(max)", maxLength: -1, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_StaffMemberId_Channel_ReadAtUtc",
                table: "Notifications",
                columns: new[] { "StaffMemberId", "Channel", "ReadAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_NextAttemptAtUtc",
                table: "Notifications",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_EventId_Type_Channel",
                table: "Notifications",
                columns: new[] { "EventId", "Type", "Channel" },
                unique: true,
                filter: "[EventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_LoanId_Type_Channel_LocalDate",
                table: "Notifications",
                columns: new[] { "LoanId", "Type", "Channel", "LocalDate" },
                unique: true,
                filter: "[LoanId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_NotificationTemplates_Type_Active",
                table: "NotificationTemplates",
                column: "Type",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");
        }
    }
}
