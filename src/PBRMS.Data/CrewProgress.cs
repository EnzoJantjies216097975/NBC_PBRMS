using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// What happens to a booking while its crew is decided. A studio production is filled
/// from the monthly roster as far as it can be, and the Booking Officer does the rest.
/// An outside production goes to the supervisors of the divisions it needs.
/// See <see cref="BookingWorkflow.CrewLead"/>.
/// </summary>
public class CrewProgress(PbrmsDbContext db, ClashChecker clashChecker, Notifier notifier)
{
    /// <summary>The EP has approved: start crewing. Must be called inside a transaction, for the clash locks.</summary>
    public async Task OpenAsync(Booking booking, Guid approvedById)
    {
        var crewLead = BookingWorkflow.CrewLead(booking.Venue);

        if (crewLead == Role.BookingOfficer)
            await FillFromRosterAsync(booking);

        if (booking.CrewSlots.All(s => s.Current is not null))
        {
            await MoveOnIfFullAsync(booking, approvedById);
            return;
        }

        // Only the people who decide are told. Supervisors hear only about outside
        // productions that need their division, and the Booking Officer only about studios.
        var open = booking.CrewSlots.Where(s => s.Current is null).ToList();
        var people = crewLead == Role.BookingOfficer
            ? await notifier.PeopleWithRoleAsync(Role.BookingOfficer)
            : await notifier.PeopleWithRoleAsync(Role.Supervisor, open.Select(s => s.DivisionId));

        notifier.Send(
            people,
            $"#{booking.ProductionNo} {booking.Title} ({NbcTime.Text(booking.StartsAt)}, {booking.Venue}) needs crew: {string.Join(", ", open.Select(s => s.PositionLabel))}.",
            booking.Id);
    }

    /// <summary>
    /// Once every slot is filled, a studio production waits for the Booking Officer to
    /// confirm. An outside production is confirmed there and then: supervisors phone their
    /// operators before deciding, so their decision is the confirmation.
    /// </summary>
    public async Task MoveOnIfFullAsync(Booking booking, Guid actorId)
    {
        if (booking.Status != BookingStatus.CrewAssignment || booking.CrewSlots.Any(s => s.Current is null))
            return;

        var studio = BookingWorkflow.CrewLead(booking.Venue) == Role.BookingOfficer;
        var next = studio ? BookingStatus.Finalisation : BookingStatus.Confirmed;

        booking.History.Add(new BookingStatusChange
        {
            FromStatus = BookingStatus.CrewAssignment,
            ToStatus = next,
            ActorId = actorId,
            Note = "All crew positions filled",
            At = DateTimeOffset.UtcNow
        });
        booking.Status = next;

        if (studio)
        {
            var bookingOfficers = await notifier.PeopleWithRoleAsync(Role.BookingOfficer);
            notifier.Send(
                bookingOfficers.Where(person => person != actorId),
                $"#{booking.ProductionNo} {booking.Title} ({NbcTime.Text(booking.StartsAt)}) has its crew and is ready to confirm.",
                booking.Id);
        }
        else
        {
            var units = await db.OrgUnits.AsNoTracking().ToListAsync();
            await notifier.TellConfirmedAsync(booking, OrgUnit.ChainUp(booking.DepartmentId, units));
        }
    }

    /// <summary>
    /// Puts the people the published roster has in this studio onto the empty slots of
    /// their division. Anyone with a clash or a warning is left for the Booking Officer to
    /// decide. Operators aren't told yet: they hear when the booking is confirmed.
    /// </summary>
    private async Task FillFromRosterAsync(Booking booking)
    {
        var empty = booking.CrewSlots.Where(s => s.Current is null).ToList();
        var candidates = await RosteredToStudioAsync(booking, empty);

        await ClashLocks.TakeAsync(db, candidates.SelectMany(c => c.Value).Select(ClashLocks.ForOperator));

        var taken = new HashSet<Guid>(booking.CrewSlots.Where(s => s.Current is not null).Select(s => s.Current!.OperatorId));

        foreach (var slot in empty)
        {
            foreach (var operatorId in candidates[slot.Id].Where(id => !taken.Contains(id)))
            {
                if ((await clashChecker.ForOperatorAsync(operatorId, slot.StartsAt, slot.EndsAt, booking)).Count > 0)
                    continue;

                slot.Assignments.Add(new CrewAssignment { OperatorId = operatorId, AssignedAt = DateTimeOffset.UtcNow });
                taken.Add(operatorId);
                break;
            }
        }
    }

    /// <summary>For each slot, the operators of its division the roster has in this studio at that time.</summary>
    private async Task<Dictionary<Guid, List<Guid>>> RosteredToStudioAsync(Booking booking, List<CrewSlot> slots)
    {
        var day = NbcTime.DateOf(booking.StartsAt);
        var cells = await clashChecker.EntriesInForce()
            .AsNoTracking()
            .Where(e => e.Date == day)
            .Select(e => new { e.OperatorId, e.ShiftCode, e.Roster.DivisionId })
            .ToListAsync();

        var divisions = slots.Select(s => s.DivisionId).Distinct().ToList();
        var legends = await db.ShiftDefinitions.AsNoTracking().Where(s => divisions.Contains(s.DivisionId)).ToListAsync();

        return slots.ToDictionary(
            slot => slot.Id,
            slot => cells
                .Where(c => c.DivisionId == slot.DivisionId)
                .Where(c => RosterRules.IsRosteredTo(
                    booking.Venue,
                    ClashChecker.ShiftsIn(c.ShiftCode, legends.Where(s => s.DivisionId == slot.DivisionId)),
                    day, slot.StartsAt, slot.EndsAt))
                .Select(c => c.OperatorId)
                .ToList());
    }
}
