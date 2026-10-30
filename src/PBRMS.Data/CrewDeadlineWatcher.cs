using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// When the daily roster falls due (16h00 the day before), tells the Booking Officer about
/// any outside production its supervisors haven't finished crewing, so they can decide it.
/// Once per production. The API runs this every minute (see MinuteChecksService).
/// </summary>
public class CrewDeadlineWatcher(PbrmsDbContext db, Notifier notifier)
{
    /// <param name="now">The time to check at. Passed in so tests don't have to wait.</param>
    public async Task CheckAsync(DateTimeOffset now)
    {
        // Productions up to and including this date have reached their deadline.
        var today = NbcTime.DateOf(now);
        var dueThrough = TimeOnly.FromTimeSpan(now.ToOffset(NbcTime.Offset).TimeOfDay) >= BookingWorkflow.DailyRosterTime
            ? today.AddDays(1)
            : today;
        var beforeTheNextDay = NbcTime.At(dueThrough.AddDays(1), TimeOnly.MinValue);

        var undecided = await db.Bookings
            .Where(b => b.Status == BookingStatus.CrewAssignment
                        && !ClashRules.Studios.Contains(b.Venue)
                        && b.StartsAt < beforeTheNextDay
                        && b.BookingOfficerToldAt == null)
            .ToListAsync();
        if (undecided.Count == 0)
            return;

        var bookingOfficers = await notifier.PeopleWithRoleAsync(Role.BookingOfficer);

        foreach (var booking in undecided)
        {
            notifier.Send(
                bookingOfficers,
                $"#{booking.ProductionNo} {booking.Title} ({NbcTime.Text(booking.StartsAt)}, {booking.Venue}) still needs crew and the daily roster is due. You can decide it now.",
                booking.Id);
            booking.BookingOfficerToldAt = now;
        }

        await db.SaveChangesAsync();
    }
}
