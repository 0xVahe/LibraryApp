using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Isbn = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookSuggestions", x => x.Id);
                    table.CheckConstraint("CK_BookSuggestions_RejectComment", "[Status] <> 3 OR ([ReviewComment] IS NOT NULL AND LEN(LTRIM(RTRIM([ReviewComment]))) > 0)");
                    table.CheckConstraint("CK_BookSuggestions_Review", "([Status] = 1 AND [ReviewedAtUtc] IS NULL AND [ReviewedByUserId] IS NULL AND [ReviewComment] IS NULL) OR ([Status] IN (2, 3) AND [ReviewedAtUtc] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BookSuggestions_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookSuggestions_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookSuggestions_ReviewedByUserId",
                table: "BookSuggestions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BookSuggestions_StaffMemberId_CreatedAtUtc_Id",
                table: "BookSuggestions",
                columns: new[] { "StaffMemberId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BookSuggestions_Status_CreatedAtUtc_Id",
                table: "BookSuggestions",
                columns: new[] { "Status", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookSuggestions");
        }
    }
}
