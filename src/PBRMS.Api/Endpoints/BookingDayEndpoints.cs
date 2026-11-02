using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// What the producer does on the day (App Flow Document, "Production day"): confirm who has
/// arrived, signal anyone who hasn't, and confirm a shoot has left. Marking the start and
/// end is a normal transition, in <see cref="BookingTransitionEndpoints"/>.
/// </summary>
public static class BookingDayEndpoints
{
    public static void MapBookingDayEndpoints(this WebApplication app)
    {
        app.MapPost("/bookings/{id:guid}/attendance", Attendance).RequireAuthorization();
        app.MapPost("/bookings/{id:guid}/signal", Signal).RequireAuthorization();
        app.MapPost("/bookings/{id:guid}/departure", Departure).RequireAuthorization();
    }

    private static async Task<IResult> Attendance(Guid id, AttendanceRequest request, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var (booking, problem) = await OpenAsync(id, principal, db);
        if (problem is not null)
            return problem;

        if (CurrentOn(booking!, request.SlotId) is not { } assignment)
            return Problems.Invalid("slotId", "Nobody is on that crew position.");

        // Marking someone twice keeps the time they really arrived.
        assignment.ArrivedAt = request.Arrived ? assignment.ArrivedAt ?? DateTimeOffset.UtcNow : null;

        await db.SaveChangesAsync();
        return Results.Ok(await BookingQueries.LoadResponseAsync(db, id));
    }

    /// <summary>Urgent, by push and SMS once those are set up (BR-15). Until then it's a notification.</summary>
    private static async Task<IResult> Signal(Guid id, SignalRequest request, ClaimsPrincipal principal, PbrmsDbContext db, Notifier notifier)
    {
        var (booking, problem) = await OpenAsync(id, principal, db);
        if (problem is not null)
            return problem;

        if (CurrentOn(booking!, request.SlotId) is not { } assignment)
            return Problems.Invalid("slotId", "Nobody is on that crew position.");
        if (assignment.ArrivedAt is not null)
            return Problems.Conflict("They've already been marked as arrived.");

        assignment.SignalledAt = DateTimeOffset.UtcNow;
        notifier.Send(
            [assignment.OperatorId],
            $"URGENT: you're needed now on #{booking!.ProductionNo} {booking.Title} ({NbcTime.Text(booking.StartsAt)}). Please contact the producer.",
            booking.Id);

        await db.SaveChangesAsync();
        return Results.Ok(await BookingQueries.LoadResponseAsync(db, id));
    }

    /// <summary>The shoot has left the building. If it had been flagged, that's cleared and the supervisors stop worrying.</summary>
    private static async Task<IResult> Departure(Guid id, ClaimsPrincipal principal, PbrmsDbContext db, Notifier notifier)
    {
        var (booking, problem) = await OpenAsync(id, principal, db);
        if (problem is not null)
            return problem;

        if (!BookingWorkflow.LeavesTheBuilding(booking!))
            return Problems.Conflict("This production doesn't leave the building.");

        if (booking!.DepartureMissedAt is not null)
        {
            notifier.Send(
                await notifier.PeopleWithRoleAsync(Role.Supervisor, booking.CrewSlots.Select(s => s.DivisionId)),
                $"#{booking.ProductionNo} {booking.Title} has left after all. Its crew stays on it.",
                booking.Id);
            booking.DepartureMissedAt = null;
        }

        booking.DepartedAt ??= DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(await BookingQueries.LoadResponseAsync(db, id));
    }

    /// <summary>The booking, if this person is its producer and it's on the day. Otherwise the problem.</summary>
    private static async Task<(Booking? Booking, IResult? Problem)> OpenAsync(Guid id, ClaimsPrincipal principal, PbrmsDbContext db)
    {
        var booking = await db.Bookings
            .Include(b => b.CrewSlots).ThenInclude(s => s.Assignments.Where(a => a.RemovedAt == null))
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null)
            return (null, Results.NotFound());
        if (booking.ProducerId != PbrmsClaims.UserIdOf(principal))
            return (null, Problems.Forbidden("Only the booking's producer can do this."));
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.InProgress))
            return (null, Problems.Conflict("This is only for a confirmed production."));

        return (booking, null);
    }

    private static CrewAssignment? CurrentOn(Booking booking, Guid slotId) =>
        booking.CrewSlots.FirstOrDefault(s => s.Id == slotId)?.Current;
}
