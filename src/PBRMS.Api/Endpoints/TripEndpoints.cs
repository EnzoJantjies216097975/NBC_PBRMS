using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// The trip rotation (BR-10). A sit-out's reason is personal, so this is only for those who
/// decide who travels: a division's supervisor, the Booking Officer, who decides at the
/// deadline, and the TV Operations Manager. Only the supervisor changes it. Each change
/// answers with the whole rotation, because one change can move everyone's turn.
/// </summary>
public static class TripEndpoints
{
    private const string Rotation = "/org-units/{divisionId:guid}/trip-rotation";

    public static void MapTripEndpoints(this WebApplication app)
    {
        app.MapGet(Rotation, ReadRotation).RequireAuthorization();
        app.MapPut(Rotation + "/{operatorId:guid}/sit-out", SetSitOut).RequireAuthorization();
        app.MapPost(Rotation + "/{operatorId:guid}/trips", LogTrip).RequireAuthorization();
        app.MapDelete(Rotation + "/{operatorId:guid}/trips/{tripId:guid}", RemoveLoggedTrip).RequireAuthorization();
    }

    private static async Task<IResult> ReadRotation(Guid divisionId, ClaimsPrincipal principal, PbrmsDbContext db, TripStandings standings)
    {
        var caller = await Caller.LoadAsync(principal, db);
        if (!Supervises(caller, divisionId) && !caller.Holds(Role.BookingOfficer, Role.TvOperationsManager))
            return Problems.Forbidden("Only this division's supervisor, the Booking Officer and the TV Operations Manager can see its trip rotation.");

        return Results.Ok(await standings.ForDivisionAsync(divisionId));
    }

    private static async Task<IResult> SetSitOut(
        Guid divisionId, Guid operatorId, TripSitOutForm form, ClaimsPrincipal principal, PbrmsDbContext db, TripStandings standings)
    {
        if (await Refusal(divisionId, principal, db) is { } refused)
            return refused;

        var role = await OperatorRoleAsync(db, divisionId, operatorId);
        if (role is null)
            return Results.NotFound();

        if (form.Reason is not null && form.Until < NbcTime.DateOf(DateTimeOffset.UtcNow))
            return Problems.Invalid("until", "The last day of sitting out can't be in the past.");

        role.TripSitOutReason = form.Reason;
        role.TripSitOutUntil = form.Reason is null ? null : form.Until;
        await db.SaveChangesAsync();

        return Results.Ok(await standings.ForDivisionAsync(divisionId));
    }

    private static async Task<IResult> LogTrip(
        Guid divisionId, Guid operatorId, LoggedTripForm form, ClaimsPrincipal principal, PbrmsDbContext db, TripStandings standings)
    {
        if (await Refusal(divisionId, principal, db) is { } refused)
            return refused;

        if (await OperatorRoleAsync(db, divisionId, operatorId) is null)
            return Results.NotFound();

        var title = form.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > 160)
            return Problems.Invalid("title", "Say where the trip was, in 160 characters or fewer.");
        if (form.To < form.From)
            return Problems.Invalid("to", "A trip can't end before it starts.");

        // A trip still to come belongs on a booking, where it gets a crew and an approval.
        if (form.To >= NbcTime.DateOf(DateTimeOffset.UtcNow))
            return Problems.Invalid("to", "Only a trip that's over can be logged by hand.");

        db.LoggedTrips.Add(new LoggedTrip
        {
            OperatorId = operatorId,
            Title = title,
            From = form.From,
            To = form.To,
            RecordedById = PbrmsClaims.UserIdOf(principal),
            RecordedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        return Results.Ok(await standings.ForDivisionAsync(divisionId));
    }

    private static async Task<IResult> RemoveLoggedTrip(
        Guid divisionId, Guid operatorId, Guid tripId, ClaimsPrincipal principal, PbrmsDbContext db, TripStandings standings)
    {
        if (await Refusal(divisionId, principal, db) is { } refused)
            return refused;

        var trip = await db.LoggedTrips.FirstOrDefaultAsync(t => t.Id == tripId && t.OperatorId == operatorId && t.RemovedAt == null);
        if (trip is null || await OperatorRoleAsync(db, divisionId, operatorId) is null)
            return Results.NotFound();

        trip.RemovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(await standings.ForDivisionAsync(divisionId));
    }

    private static bool Supervises(Caller caller, Guid divisionId) =>
        caller.Roles.Any(r => r.Role == Role.Supervisor && r.OrgUnitId == divisionId);

    /// <summary>Why this person may not change the rotation, or null if they may.</summary>
    private static async Task<IResult?> Refusal(Guid divisionId, ClaimsPrincipal principal, PbrmsDbContext db) =>
        Supervises(await Caller.LoadAsync(principal, db), divisionId)
            ? null
            : Problems.Forbidden("Only this division's supervisor can change its trip rotation.");

    private static Task<RoleAssignment?> OperatorRoleAsync(PbrmsDbContext db, Guid divisionId, Guid operatorId) =>
        db.RoleAssignments.FirstOrDefaultAsync(r => r.UserId == operatorId && r.OrgUnitId == divisionId && r.Role == Role.Operator);
}
