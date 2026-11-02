using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// The clock-driven parts of production day (App Flow Document): the call an hour before the
/// start, and flagging a shoot nobody has confirmed as leaving (BR-11). The API runs this
/// every minute (see MinuteChecksService).
/// </summary>
/// <remarks>
/// These go out as notifications. They reach phones by push or SMS once those are set up (BR-15).
/// </remarks>
public class ProductionDayWatcher(PbrmsDbContext db, Notifier notifier)
{
    /// <param name="now">The time to check at. Passed in so tests don't have to wait.</param>
    public async Task CheckAsync(DateTimeOffset now)
    {
        await CallCrewAsync(now);
        await FlagShootsThatDidntLeaveAsync(now);
        await db.SaveChangesAsync();
    }

    /// <summary>Calls everyone on a production starting within the hour, once each.</summary>
    private async Task CallCrewAsync(DateTimeOffset now)
    {
        var startsBy = now + BookingWorkflow.CrewCallNotice;

        var due = await db.Bookings
            .Include(b => b.CrewSlots).ThenInclude(s => s.Assignments.Where(a => a.RemovedAt == null))
            .Where(b => b.Status == BookingStatus.Confirmed && b.CrewCalledAt == null && b.StartsAt > now && b.StartsAt <= startsBy)
            .ToListAsync();

        foreach (var booking in due)
        {
            var crew = booking.CrewSlots.Where(s => s.Current is not null).Select(s => s.Current!.OperatorId);

            notifier.Send(
                crew.Append(booking.ProducerId),
                $"Call: #{booking.ProductionNo} {booking.Title} starts at {NbcTime.Text(booking.StartsAt)}.",
                booking.Id);
            booking.CrewCalledAt = now;
        }
    }

    /// <summary>
    /// Tells the supervisors and the producer. The crew stays booked until a supervisor has
    /// checked and cancelled it (see <see cref="BookingWorkflow.MayCancel"/>). A shoot marked as
    /// started isn't Confirmed any more, so it's clearly under way and never flagged.
    /// </summary>
    private async Task FlagShootsThatDidntLeaveAsync(DateTimeOffset now)
    {
        var startedBy = now - BookingWorkflow.DepartureGrace;

        var flagged = await db.Bookings
            .Include(b => b.CrewSlots)
            .Where(b => b.Status == BookingStatus.Confirmed
                        && (b.IsTrip || BookingWorkflow.OutsideVenues.Contains(b.Venue))
                        && b.StartsAt <= startedBy
                        && b.DepartedAt == null
                        && b.DepartureMissedAt == null)
            .ToListAsync();

        foreach (var booking in flagged)
        {
            var supervisors = await notifier.PeopleWithRoleAsync(Role.Supervisor, booking.CrewSlots.Select(s => s.DivisionId));

            notifier.Send(
                supervisors.Append(booking.ProducerId),
                $"#{booking.ProductionNo} {booking.Title} hasn't been confirmed as leaving. It was booked for {NbcTime.Text(booking.StartsAt)}. "
                + "If it didn't go, cancel it to release its crew.",
                booking.Id);
            booking.DepartureMissedAt = now;
        }
    }
}
