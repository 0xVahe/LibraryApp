using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationCopyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CopyId",
                table: "Reservations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_ReadyForPickup_CopyId",
                table: "Reservations",
                column: "CopyId",
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_ReadyForPickup_CopyId",
                table: "Reservations",
                sql: "[Status] <> 2 OR [CopyId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_BookCopies_CopyId",
                table: "Reservations",
                column: "CopyId",
                principalTable: "BookCopies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_BookCopies_CopyId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "UX_Reservations_ReadyForPickup_CopyId",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_ReadyForPickup_CopyId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CopyId",
                table: "Reservations");
        }
    }
}
