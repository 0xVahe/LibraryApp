using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxLoans = table.Column<int>(type: "int", nullable: false),
                    LoanDays = table.Column<int>(type: "int", nullable: false),
                    MaxRenewals = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffCategories", x => x.Id);
                    table.CheckConstraint("CK_StaffCategories_LoanDaysPositive", "[LoanDays] > 0");
                    table.CheckConstraint("CK_StaffCategories_MaxLoansPositive", "[MaxLoans] > 0");
                    table.CheckConstraint("CK_StaffCategories_MaxRenewalsNotNegative", "[MaxRenewals] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "StaffMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorporateEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LineManagerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StaffCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JoinedLibraryAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffMembers_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffMembers_StaffCategories_StaffCategoryId",
                        column: x => x.StaffCategoryId,
                        principalTable: "StaffCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StaffCategories_Name",
                table: "StaffCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_DepartmentId",
                table: "StaffMembers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_StaffCategoryId",
                table: "StaffMembers",
                column: "StaffCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_Status",
                table: "StaffMembers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_StaffMembers_CorporateEmail",
                table: "StaffMembers",
                column: "CorporateEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StaffMembers_EmployeeId",
                table: "StaffMembers",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StaffMembers_UserId",
                table: "StaffMembers",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffMembers");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "StaffCategories");
        }
    }
}
