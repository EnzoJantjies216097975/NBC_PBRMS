using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBRMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductionDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArrivedAt",
                table: "CrewAssignments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignalledAt",
                table: "CrewAssignments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CrewCalledAt",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DepartedAt",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DepartureMissedAt",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivedAt",
                table: "CrewAssignments");

            migrationBuilder.DropColumn(
                name: "SignalledAt",
                table: "CrewAssignments");

            migrationBuilder.DropColumn(
                name: "CrewCalledAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepartedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepartureMissedAt",
                table: "Bookings");
        }
    }
}
