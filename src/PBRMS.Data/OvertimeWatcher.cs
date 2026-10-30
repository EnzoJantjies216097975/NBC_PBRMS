using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Tells the people responsible when a production goes overtime: still running
/// <see cref="BookingWorkflow.OverrunThreshold"/> after its booked end. It goes to the
/// supervisors of its crew divisions, once per
/// production. The API runs this every minute (see MinuteChecksService).
/// </summary>
/// <remarks>
/// Only a production the producer marked as started can be seen running. This alert
/// isn't the overtime record itself: that is computed from logged times later (TR-07).
/// </remarks>
public class OvertimeWatcher(PbrmsDbContext db, Notifier notifier)
{
    /// <param name="now">The time to check at. Passed in so tests don't have to wait.</param>
    public async Task CheckAsync(DateTimeOffset now)
    {
        var bookedEndBefore = now - BookingWorkflow.OverrunThreshold;

        var overtime = await db.Bookings
            .Include(b => b.CrewSlots)
            .Where(b => b.Status == BookingStatus.InProgress
                        && b.EndsAt <= bookedEndBefore
                        && b.OvertimeNotifiedAt == null)
            .ToListAsync();
        if (overtime.Count == 0)
            return;

        foreach (var booking in overtime)
        {
            var supervisors = await notifier.PeopleWithRoleAsync(Role.Supervisor, booking.CrewSlots.Select(s => s.DivisionId));

            notifier.Send(
                supervisors,
                $"#{booking.ProductionNo} {booking.Title} has gone overtime. It was booked to end at {NbcTime.Text(booking.EndsAt)}.",
                booking.Id);
            booking.OvertimeNotifiedAt = now;
        }

        await db.SaveChangesAsync();
    }
}
