using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PBRMS.Api.Auth;
using PBRMS.Data;
using PBRMS.Domain;

namespace PBRMS.Api.Endpoints;

/// <summary>
/// PUT /bookings/{id}/crew: supervisors put operators on their division's crew slots.
/// When the last slot is filled the booking moves on to the Booking Officer by itself,
/// so it reaches them only when every division has assigned crew (FR-BOOK-06).
/// </summary>
public static class BookingCrewEndpoints
{
    public static void MapBookingCrewEndpoints(this WebApplication app)
    {
        app.MapPut("/bookings/{id:guid}/crew", AssignCrew)
            .RequireAuthorization(p => p.RequireRole(nameof(Role.Supervisor), nameof(Role.BookingOfficer)));
    }

    private static async Task<IResult> AssignCrew(Guid id, CrewRequest request, ClaimsPrincipal principal, PbrmsDbContext db, ClashChecker clashChecker)
    {
        // Everything happens in one transaction, so the clash locks are held until the save is committed.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var booking = await db.Bookings
            .Include(b => b.CrewSlots).ThenInclude(s => s.Assignments.Where(a => a.RemovedAt == null))
            .FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
            return Results.NotFound();

        // Crew changes after confirmation are a separate flow with its own warnings and notifications.
        if (booking.Status is not (BookingStatus.CrewAssignment or BookingStatus.Finalisation))
            return Problems.Conflict($"Crew can't be changed while a booking is in {booking.Status}.");

        var caller = await Caller.LoadAsync(principal, db);

        // 1. Check each change is allowed, before touching anything.
        var changes = new List<(CrewSlot Slot, Guid? OperatorId)>();
        foreach (var change in request.Assignments ?? [])
        {
            var slot = booking.CrewSlots.FirstOrDefault(s => s.Id == change.SlotId);
            if (slot is null)
                return Problems.Invalid("slotId", "That crew position isn't on this booking.");
            if (!BookingWorkflow.MayStaff(slot.DivisionId, caller.Roles))
                return Problems.Forbidden("You can only assign crew in your own division.");

            if (change.OperatorId is Guid operatorId && !await IsOperatorInDivisionAsync(db, operatorId, slot.DivisionId))
                return Problems.Invalid("operatorId", "That person isn't an active operator in this position's division.");

            changes.Add((slot, change.OperatorId));
        }

        // 2. Lock the operators, then check they're free (see ClashLocks for why in that order).
        var newOperators = changes.Where(c => c.OperatorId is not null).ToList();
        await ClashLocks.TakeAsync(db, newOperators.Select(c => ClashLocks.ForOperator(c.OperatorId!.Value)));

        var clashes = new List<Clash>();
        foreach (var (slot, operatorId) in newOperators)
            clashes.AddRange(await clashChecker.ForOperatorAsync(operatorId!.Value, slot.StartsAt, slot.EndsAt, booking.Id));
        if (clashes.Count > 0)
            return Problems.Clashes("An operator is already on another production at that time.", clashes.Distinct().ToList());

        // 3. Apply the changes. The old assignment is kept and marked removed, not deleted.
        var now = DateTimeOffset.UtcNow;
        foreach (var (slot, operatorId) in changes)
        {
            if (slot.Current?.OperatorId == operatorId)
                continue;

            slot.Current?.RemovedAt = now;
            if (operatorId is not null)
                slot.Assignments.Add(new CrewAssignment { OperatorId = operatorId.Value, AssignedById = caller.UserId, AssignedAt = now });
        }

        // 4. The last empty slot was just filled: pass the booking to the Booking Officer.
        if (booking.Status == BookingStatus.CrewAssignment && booking.CrewSlots.All(s => s.Current is not null))
        {
            booking.History.Add(new BookingStatusChange
            {
                FromStatus = BookingStatus.CrewAssignment,
                ToStatus = BookingStatus.Finalisation,
                ActorId = caller.UserId,
                Note = "All crew positions filled",
                At = now
            });
            booking.Status = BookingStatus.Finalisation;
        }

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException) // also covers DbUpdateConcurrencyException
        {
            // Someone filled the same slot, or changed the booking, at the same moment.
            return Problems.Conflict("Someone else changed this booking at the same moment. Reload it and try again.");
        }

        return Results.Ok(await BookingQueries.LoadResponseAsync(db, id));
    }

    private static Task<bool> IsOperatorInDivisionAsync(PbrmsDbContext db, Guid userId, Guid divisionId) =>
        db.RoleAssignments.AnyAsync(r =>
            r.UserId == userId && r.Role == Role.Operator && r.OrgUnitId == divisionId && r.User.IsActive);
}
