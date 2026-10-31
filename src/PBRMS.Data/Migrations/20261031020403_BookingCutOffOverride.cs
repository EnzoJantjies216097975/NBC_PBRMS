using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBRMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class BookingCutOffOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LateAllowedById",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateAllowedReason",
                table: "Bookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_LateAllowedById",
                table: "Bookings",
                column: "LateAllowedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Users_LateAllowedById",
                table: "Bookings",
                column: "LateAllowedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Users_LateAllowedById",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_LateAllowedById",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "LateAllowedById",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "LateAllowedReason",
                table: "Bookings");
        }
    }
}
