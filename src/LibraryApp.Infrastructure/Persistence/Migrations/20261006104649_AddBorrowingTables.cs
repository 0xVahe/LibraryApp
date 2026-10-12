using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookCopyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReturnedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenewalCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                    table.CheckConstraint("CK_Loans_RenewalCount_NonNegative", "[RenewalCount] >= 0");
                    table.ForeignKey(
                        name: "FK_Loans_BookCopies_BookCopyId",
                        column: x => x.BookCopyId,
                        principalTable: "BookCopies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loans_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReadyUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QueuePosition = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_QueuePosition", "([Status] = 1 AND [QueuePosition] >= 1) OR ([Status] <> 1 AND [QueuePosition] = 0)");
                    table.ForeignKey(
                        name: "FK_Reservations_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PenaltyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WaivedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyRecords", x => x.Id);
                    table.CheckConstraint("CK_PenaltyRecords_Points", "[Points] BETWEEN 0 AND 10");
                    table.CheckConstraint("CK_PenaltyRecords_WaivedReason", "[Status] <> 3 OR ([WaivedReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WaivedReason]))) > 0)");
                    table.ForeignKey(
                        name: "FK_PenaltyRecords_Loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "Loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PenaltyRecords_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_StaffMemberId_Status",
                table: "Loans",
                columns: new[] { "StaffMemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_Status_DueAtUtc",
                table: "Loans",
                columns: new[] { "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Loans_Open_BookCopyId",
                table: "Loans",
                column: "BookCopyId",
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyRecords_LoanId",
                table: "PenaltyRecords",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyRecords_StaffMemberId_Status_ExpiresAtUtc",
                table: "PenaltyRecords",
                columns: new[] { "StaffMemberId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BookId_Status_CreatedAtUtc",
                table: "Reservations",
                columns: new[] { "BookId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_StaffMemberId_Status",
                table: "Reservations",
                columns: new[] { "StaffMemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_Active_StaffMemberId_BookId",
                table: "Reservations",
                columns: new[] { "StaffMemberId", "BookId" },
                unique: true,
                filter: "[Status] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenaltyRecords");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "Loans");
        }
    }
}
