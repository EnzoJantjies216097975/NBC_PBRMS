using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBRMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Production = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RequesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SuggestedStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SuggestedEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SuggestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppSessions_Users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSessions_Users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSessions_Users_SuggestedById",
                        column: x => x.SuggestedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSessions_OperatorId_StartsAt",
                table: "AppSessions",
                columns: new[] { "OperatorId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSessions_RequesterId",
                table: "AppSessions",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSessions_SuggestedById",
                table: "AppSessions",
                column: "SuggestedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSessions");
        }
    }
}
