using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// GET /reports/off-roster-work: who was booked on a day off or outside their shift, for
/// the TV Operations Manager. Supervisors see their own divisions. Work that didn't
/// end up happening (the operator was replaced, or the booking cancelled) is left out.
/// The records are kept for HR too. HR has no report yet; it can be given access here later.
/// </summary>
public static class OffRosterEndpoints
{
    public record OffRosterResponse(
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        Guid OperatorId,
        string Operator,
        string Division,
        int ProductionNo,
        string Title,
        ClashKind Kind,
        string Reason,
        string RecordedBy,
        DateTimeOffset RecordedAt);

    public static void MapOffRosterEndpoints(this WebApplication app)
    {
        app.MapGet("/reports/off-roster-work", Report).RequireAuthorization(p => p.RequireRole(
            nameof(Role.Supervisor), nameof(Role.TvOperationsManager), nameof(Role.Management), nameof(Role.Admin)));
    }

    /// <param name="divisionId">Leave empty for every division the person can see.</param>
    private static async Task<List<OffRosterResponse>> Report(DateOnly from, DateOnly to, Guid? divisionId, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var caller = await Caller.LoadAsync(principal, db);

        // Anyone who isn't only a supervisor sees all divisions.
        var seesAll = caller.Holds(Role.TvOperationsManager, Role.Management, Role.Admin)
                      || caller.Roles.Any(r => r is { Role: Role.Supervisor, OrgUnitId: null });
        var ownDivisions = caller.Roles.Where(r => r.Role == Role.Supervisor && r.OrgUnitId is not null).Select(r => r.OrgUnitId!.Value).ToList();

        var (start, end) = (NbcTime.At(from, TimeOnly.MinValue), NbcTime.At(to.AddDays(1), TimeOnly.MinValue));

        return await db.OffRosterWork.AsNoTracking()
            .Where(w => w.CrewAssignment.RemovedAt == null
                        && w.CrewAssignment.CrewSlot.Booking.Status != BookingStatus.Cancelled
                        && w.CrewAssignment.CrewSlot.StartsAt >= start && w.CrewAssignment.CrewSlot.StartsAt < end
                        && (seesAll || ownDivisions.Contains(w.CrewAssignment.CrewSlot.DivisionId))
                        && (divisionId == null || w.CrewAssignment.CrewSlot.DivisionId == divisionId))
            .OrderBy(w => w.CrewAssignment.CrewSlot.StartsAt)
            .Select(w => new OffRosterResponse(
                w.CrewAssignment.CrewSlot.StartsAt,
                w.CrewAssignment.CrewSlot.EndsAt,
                w.CrewAssignment.OperatorId,
                w.CrewAssignment.Operator.FirstName + " " + w.CrewAssignment.Operator.Surname,
                w.CrewAssignment.CrewSlot.Division.Name,
                w.CrewAssignment.CrewSlot.Booking.ProductionNo,
                w.CrewAssignment.CrewSlot.Booking.Title,
                w.Kind,
                w.Reason,
                w.RecordedBy.FirstName + " " + w.RecordedBy.Surname,
                w.RecordedAt))
            .ToListAsync();
    }
}
