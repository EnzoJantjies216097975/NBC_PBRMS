using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBRMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "ProductionNumbers");

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionNo = table.Column<int>(type: "int", nullable: false, defaultValueSql: "NEXT VALUE FOR ProductionNumbers"),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProducerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    VenueText = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    CallAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AirAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsTrip = table.Column<bool>(type: "bit", nullable: false),
                    IsOb = table.Column<bool>(type: "bit", nullable: false),
                    AllocationCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_OrgUnits_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Users_ProducerId",
                        column: x => x.ProducerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingStatusChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingStatusChanges_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingStatusChanges_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrewSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SpecialEquipment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrewSlots_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CrewSlots_OrgUnits_DivisionId",
                        column: x => x.DivisionId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_DepartmentId",
                table: "Bookings",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ProducerId",
                table: "Bookings",
                column: "ProducerId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ProductionNo",
                table: "Bookings",
                column: "ProductionNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status_DepartmentId",
                table: "Bookings",
                columns: new[] { "Status", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Venue_StartsAt",
                table: "Bookings",
                columns: new[] { "Venue", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusChanges_ActorId",
                table: "BookingStatusChanges",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusChanges_BookingId",
                table: "BookingStatusChanges",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_CrewSlots_BookingId",
                table: "CrewSlots",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_CrewSlots_DivisionId",
                table: "CrewSlots",
                column: "DivisionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingStatusChanges");

            migrationBuilder.DropTable(
                name: "CrewSlots");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropSequence(
                name: "ProductionNumbers");
        }
    }
}
