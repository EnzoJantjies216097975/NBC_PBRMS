using Microsoft.EntityFrameworkCore;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// What the crew picker and the monthly roster need: who is in a division, and whether a
/// person is free. Only supervisors and the Booking Officer can see what someone is booked on.
/// </summary>
public static class OperatorEndpoints
{
    public record OperatorResponse(Guid Id, string FirstName, string Surname);

    public static void MapOperatorEndpoints(this WebApplication app)
    {
        string[] crewers = [nameof(Role.Supervisor), nameof(Role.BookingOfficer)];

        // Active operators in a TV Ops division. Names only, because they head the roster everyone can read.
        app.MapGet("/org-units/{divisionId:guid}/operators", async (Guid divisionId, PbrmsDbContext db) =>
            await db.RoleAssignments.AsNoTracking()
                .Where(r => r.OrgUnitId == divisionId && r.Role == Role.Operator && r.User.IsActive)
                .OrderBy(r => r.User.Surname).ThenBy(r => r.User.FirstName)
                .Select(r => new OperatorResponse(r.UserId, r.User.FirstName, r.User.Surname))
                .ToListAsync())
            .RequireAuthorization();

        // Everything this operator is on during a period.
        app.MapGet("/operators/{id:guid}/conflicts", async (Guid id, DateTimeOffset from, DateTimeOffset to, ClashChecker clashChecker) =>
            await clashChecker.ForOperatorAsync(id, from, to))
            .RequireAuthorization(p => p.RequireRole(crewers));
    }
}
