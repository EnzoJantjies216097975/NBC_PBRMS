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
///
/// A clash (on leave, on another production) stops the assignment. A warning (a day
/// off, rostered to another studio) goes back to the person assigning, who can go ahead
/// by sending the request again with acknowledgeWarnings: true. Going ahead notifies
/// the people affected. See <see cref="RosterRules"/>.
/// </summary>
public static class BookingCrewEndpoints
{
    public static void MapBookingCrewEndpoints(this WebApplication app)
    {
        app.MapPut("/bookings/{id:guid}/crew", AssignCrew)
            .RequireAuthorization(p => p.RequireRole(nameof(Role.Supervisor), nameof(Role.BookingOfficer)));
    }

    private static async Task<IResult> AssignCrew(Guid id, CrewRequest request, ClaimsPrincipal principal, PbrmsDbContext db, ClashChecker clashChecker, Notifier notifier)
    {
        // Everything happens in one transaction, so the clash locks are held until the save is committed.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var booking = await db.Bookings
            .Include(b => b.CrewSlots).ThenInclude(s => s.Assignments.Where(a => a.RemovedAt == null))
            .FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
            return Results.NotFound();

        if (booking.Status is not (BookingStatus.CrewAssignment or BookingStatus.Finalisation or BookingStatus.Confirmed))
            return Problems.Conflict($"Crew can't be changed while a booking is in {booking.Status}.");

        var caller = await Caller.LoadAsync(principal, db);

        // 1. Check each change is allowed, before touching anything.
        var (changes, problem) = await ReadChangesAsync(request, booking, caller, db);
        if (problem is not null)
            return problem;

        // 2. Lock the operators, then check they're free (see ClashLocks for why in that order).
        var newOperators = changes.Where(c => c.OperatorId is not null).ToList();
        await ClashLocks.TakeAsync(db, newOperators.Select(c => ClashLocks.ForOperator(c.OperatorId.Value)));
        var found = await FindClashesAsync(newOperators, booking, clashChecker);

        var blocking = found.Select(f => f.Clash).Where(c => !c.IsWarning).Distinct().ToList();
        if (blocking.Count > 0)
            return Problems.Clashes("An operator can't work this position at that time.", blocking);

        var warnings = found.Where(f => f.Clash.IsWarning).ToList();
        if (warnings.Count > 0 && !request.AcknowledgeWarnings)
            return Problems.Warnings("Check these before going ahead.", warnings.Select(w => w.Clash).Distinct().ToList());

        // 3. Apply the changes, and pass the booking on if that filled the last slot.
        var replaced = ApplyChanges(changes, caller.UserId);
        PassOnIfFull(booking, caller.UserId);

        // 4. Tell the people affected.
        foreach (var (slot, operatorId, warning) in warnings)
            await TellAboutWarningAsync(notifier, booking, slot, operatorId, warning, caller.UserId);

        if (booking.Status == BookingStatus.Confirmed)
            TellAboutCrewChange(notifier, booking, replaced, newOperators.Select(c => c.OperatorId.Value));

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

    /// <summary>
    /// Turns the request into the changes to make, or the problem with it. Putting someone
    /// back on a slot they already hold changes nothing, so it's skipped.
    /// </summary>
    private static async Task<(List<(CrewSlot Slot, Guid? OperatorId)> Changes, IResult? Problem)> ReadChangesAsync(
        CrewRequest request, Booking booking, Caller caller, PbrmsDbContext db)
    {
        var changes = new List<(CrewSlot, Guid?)>();

        foreach (var change in request.Assignments ?? [])
        {
            var slot = booking.CrewSlots.FirstOrDefault(s => s.Id == change.SlotId);
            if (slot is null)
                return (changes, Problems.Invalid("slotId", "That crew position isn't on this booking."));
            if (!BookingWorkflow.MayStaff(slot.DivisionId, caller.Roles))
                return (changes, Problems.Forbidden("You can only assign crew in your own division."));
            if (slot.Current?.OperatorId == change.OperatorId)
                continue;

            if (change.OperatorId is Guid operatorId && !await IsOperatorInDivisionAsync(db, operatorId, slot.DivisionId))
                return (changes, Problems.Invalid("operatorId", "That person isn't an active operator in this position's division."));

            // A confirmed crew list stays full: replace the operator rather than removing them.
            if (booking.Status == BookingStatus.Confirmed && change.OperatorId is null)
                return (changes, Problems.Invalid("operatorId", "A confirmed position can't be left empty. Name who replaces this operator."));

            changes.Add((slot, change.OperatorId));
        }

        return (changes, null);
    }

    /// <summary>Every clash and warning for the operators being added, with where each one came from.</summary>
    private static async Task<List<(CrewSlot Slot, Guid OperatorId, Clash Clash)>> FindClashesAsync(
        List<(CrewSlot Slot, Guid? OperatorId)> newOperators, Booking booking, ClashChecker clashChecker)
    {
        var found = new List<(CrewSlot, Guid, Clash)>();

        foreach (var (slot, operatorId) in newOperators)
        {
            var clashes = await clashChecker.ForOperatorAsync(operatorId!.Value, slot.StartsAt, slot.EndsAt, booking);
            found.AddRange(clashes.Select(clash => (slot, operatorId.Value, clash)));
        }

        return found;
    }

    /// <summary>
    /// Puts the changes on the slots. The old assignment is kept and marked removed, not
    /// deleted. Returns who was taken off.
    /// </summary>
    private static List<Guid> ApplyChanges(List<(CrewSlot Slot, Guid? OperatorId)> changes, Guid assignedBy)
    {
        var now = DateTimeOffset.UtcNow;
        var replaced = new List<Guid>();

        foreach (var (slot, operatorId) in changes)
        {
            if (slot.Current is { } current)
            {
                replaced.Add(current.OperatorId);
                current.RemovedAt = now;
            }

            if (operatorId is not null)
                slot.Assignments.Add(new CrewAssignment { OperatorId = operatorId.Value, AssignedById = assignedBy, AssignedAt = now });
        }

        return replaced;
    }

    /// <summary>Once every slot is filled, the booking moves on to the Booking Officer.</summary>
    private static void PassOnIfFull(Booking booking, Guid actorId)
    {
        if (booking.Status != BookingStatus.CrewAssignment || booking.CrewSlots.Any(s => s.Current is null))
            return;

        booking.History.Add(new BookingStatusChange
        {
            FromStatus = BookingStatus.CrewAssignment,
            ToStatus = BookingStatus.Finalisation,
            ActorId = actorId,
            Note = "All crew positions filled",
            At = DateTimeOffset.UtcNow
        });
        booking.Status = BookingStatus.Finalisation;
    }

    /// <summary>
    /// After someone went ahead despite a warning. Calling someone in tells that operator,
    /// plus the division's supervisors and the Booking Officer. Moving someone away from
    /// their rostered studio tells the supervisors, who must then sort out that studio's cover.
    /// The person who made the change already saw the warning, so they aren't told again.
    /// </summary>
    private static async Task TellAboutWarningAsync(Notifier notifier, Booking booking, CrewSlot slot, Guid operatorId, Clash warning, Guid actorId)
    {
        var production = $"#{booking.ProductionNo} {booking.Title} ({NbcTime.Text(slot.StartsAt)})";
        var supervisors = await notifier.PeopleWithRoleAsync(Role.Supervisor, slot.DivisionId);

        if (warning.Kind == ClashKind.CallIn)
        {
            notifier.Send([operatorId], $"You have been called in on your day off for {production}.", booking.Id);

            var bookingOfficers = await notifier.PeopleWithRoleAsync(Role.BookingOfficer);
            notifier.Send(
                supervisors.Concat(bookingOfficers).Where(person => person != actorId),
                $"{warning.Who} has been called in on a day off for {production}.",
                booking.Id);
        }
        else if (warning.Kind == ClashKind.RosteredElsewhere)
        {
            notifier.Send(
                supervisors.Where(person => person != actorId),
                $"{warning.Who} is now on {production} at {booking.Venue}. {warning.Title}. Please make the changes needed to cover it.",
                booking.Id);
        }
    }

    /// <summary>A confirmed crew changed: tell who came off, who went on, and the producer (App Flow Document, "Crew change after confirmation").</summary>
    private static void TellAboutCrewChange(Notifier notifier, Booking booking, IEnumerable<Guid> removed, IEnumerable<Guid> added)
    {
        var production = $"#{booking.ProductionNo} {booking.Title} ({NbcTime.Text(booking.StartsAt)})";
        notifier.Send(removed, $"You are no longer on {production}.", booking.Id);
        notifier.Send(added, $"You have been added to {production}.", booking.Id);
        notifier.Send([booking.ProducerId], $"The crew on {production} has changed.", booking.Id);
    }

    private static Task<bool> IsOperatorInDivisionAsync(PbrmsDbContext db, Guid userId, Guid divisionId) =>
        db.RoleAssignments.AnyAsync(r =>
            r.UserId == userId && r.Role == Role.Operator && r.OrgUnitId == divisionId && r.User.IsActive);
}
