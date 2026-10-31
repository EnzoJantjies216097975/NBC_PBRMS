using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBRMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppWalkIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWalkIn",
                table: "AppSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsWalkIn",
                table: "AppSessions");
        }
    }
}
