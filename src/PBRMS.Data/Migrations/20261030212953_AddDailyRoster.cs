using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBRMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BookingOfficerToldAt",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DailyRosters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Announcement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRosters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyRosters_Users_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyRosterLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyRosterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Hidden = table.Column<bool>(type: "bit", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRosterLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyRosterLine_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyRosterLine_DailyRosters_DailyRosterId",
                        column: x => x.DailyRosterId,
                        principalTable: "DailyRosters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyRosterCrew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyRosterLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Division = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(170)", maxLength: 170, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRosterCrew", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyRosterCrew_DailyRosterLine_DailyRosterLineId",
                        column: x => x.DailyRosterLineId,
                        principalTable: "DailyRosterLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRosterCrew_DailyRosterLineId",
                table: "DailyRosterCrew",
                column: "DailyRosterLineId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRosterLine_BookingId",
                table: "DailyRosterLine",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRosterLine_DailyRosterId",
                table: "DailyRosterLine",
                column: "DailyRosterId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRosters_Date",
                table: "DailyRosters",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyRosters_UpdatedById",
                table: "DailyRosters",
                column: "UpdatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyRosterCrew");

            migrationBuilder.DropTable(
                name: "DailyRosterLine");

            migrationBuilder.DropTable(
                name: "DailyRosters");

            migrationBuilder.DropColumn(
                name: "BookingOfficerToldAt",
                table: "Bookings");
        }
    }
}
