using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBRMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProductionManagerStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NBC has no Production Managers, so the PM review stage and role are gone. Existing
            // rows must follow, because a status or role the code no longer knows can't be read.
            // A step in and out of PM review becomes a step to the EP, and the PM's own approval
            // (EpReview to EpReview, after this rewrite) is dropped.
            migrationBuilder.Sql("UPDATE Bookings SET Status = 'EpReview' WHERE Status = 'PmReview'");
            migrationBuilder.Sql("UPDATE BookingStatusChanges SET FromStatus = 'EpReview' WHERE FromStatus = 'PmReview'");
            migrationBuilder.Sql("UPDATE BookingStatusChanges SET ToStatus = 'EpReview' WHERE ToStatus = 'PmReview'");
            migrationBuilder.Sql("DELETE FROM BookingStatusChanges WHERE FromStatus = ToStatus");

            // The HR role goes too, for now: HR doesn't need a report yet.
            migrationBuilder.Sql("DELETE FROM RoleAssignments WHERE Role IN ('ProductionManager', 'Hr')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the old stage and roles no longer exist in the code.
        }
    }
}
