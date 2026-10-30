using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// The one clash check (TR-01): is this operator or studio already taken? The crew
/// picker, the booking officer's clash list and the confirm step all call this, so they
/// can never disagree. What counts as a clash is defined in <see cref="ClashRules"/>.
/// </summary>
public class ClashChecker(PbrmsDbContext db)
{
    /// <summary>
    /// Other productions this operator is on during the given period.
    /// An operator may hold two positions on the same production, so pass
    /// <paramref name="exceptBookingId"/> when checking a crew list for that booking.
    /// </summary>
    public async Task<List<Clash>> ForOperatorAsync(Guid operatorId, DateTimeOffset from, DateTimeOffset to, Guid? exceptBookingId = null)
    {
        return await db.CrewAssignments
            .AsNoTracking()
            .Where(a => a.OperatorId == operatorId
                        && a.RemovedAt == null
                        && a.CrewSlot.BookingId != exceptBookingId
                        && ClashRules.StatusesHoldingCrew.Contains(a.CrewSlot.Booking.Status)
                        // Same as ClashRules.Overlaps, written out so SQL Server can run it.
                        && a.CrewSlot.StartsAt < to && from < a.CrewSlot.EndsAt)
            .Select(a => new Clash(
                ClashKind.Operator,
                a.Operator.FirstName + " " + a.Operator.Surname,
                a.CrewSlot.Booking.ProductionNo,
                a.CrewSlot.Booking.Title,
                a.CrewSlot.StartsAt,
                a.CrewSlot.EndsAt))
            .ToListAsync();
    }

    /// <summary>Other productions in this booking's studio during its hours.</summary>
    public async Task<List<Clash>> ForVenueAsync(Booking booking)
    {
        if (!ClashRules.VenueCanClash(booking.Venue))
            return [];

        return await db.Bookings
            .AsNoTracking()
            .Where(b => b.Id != booking.Id
                        && b.Venue == booking.Venue
                        && ClashRules.StatusesHoldingVenue.Contains(b.Status)
                        // Same as ClashRules.Overlaps, written out so SQL Server can run it.
                        && b.StartsAt < booking.EndsAt && booking.StartsAt < b.EndsAt)
            .Select(b => new Clash(ClashKind.Venue, booking.Venue.ToString(), b.ProductionNo, b.Title, b.StartsAt, b.EndsAt))
            .ToListAsync();
    }

    /// <summary>
    /// Every clash a booking has: its studio, and every operator on its crew.
    /// The booking must be loaded with its crew slots and their current assignments.
    /// </summary>
    public async Task<List<Clash>> ForBookingAsync(Booking booking)
    {
        var clashes = await ForVenueAsync(booking);

        foreach (var slot in booking.CrewSlots.Where(s => s.Current is not null))
            clashes.AddRange(await ForOperatorAsync(slot.Current!.OperatorId, slot.StartsAt, slot.EndsAt, booking.Id));

        return clashes.Distinct().ToList();
    }
}
