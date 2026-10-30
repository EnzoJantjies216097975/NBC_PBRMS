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
    /// Why this operator can't work the given period: other productions they are on, and
    /// what the published roster says.
    /// An operator may hold two positions on the same production, so pass
    /// <paramref name="exceptBookingId"/> when checking a crew list for that booking.
    /// </summary>
    /// <param name="forTrip">The period is for an out-of-town booking, which someone rostered away can work.</param>
    public async Task<List<Clash>> ForOperatorAsync(Guid operatorId, DateTimeOffset from, DateTimeOffset to, Guid? exceptBookingId = null, bool forTrip = false)
    {
        var clashes = await OtherProductionsAsync(operatorId, from, to, exceptBookingId);
        clashes.AddRange(await RosterClashesAsync(operatorId, from, to, forTrip));
        return clashes;
    }

    private async Task<List<Clash>> OtherProductionsAsync(Guid operatorId, DateTimeOffset from, DateTimeOffset to, Guid? exceptBookingId)
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

    /// <summary>
    /// What the rosters in force say about this period. Only a published roster counts:
    /// a draft may still change, and a blank cell (no entry) is unknown, not a clash.
    /// </summary>
    private async Task<List<Clash>> RosterClashesAsync(Guid operatorId, DateTimeOffset from, DateTimeOffset to, bool forTrip)
    {
        var days = RosterRules.DaysCovered(from, to);
        var (firstDay, lastDay) = (days[0], days[^1]);

        var entries = await db.RosterEntries
            .AsNoTracking()
            .Where(e => e.OperatorId == operatorId
                        && e.Date >= firstDay && e.Date <= lastDay
                        && e.Roster.Status == RosterStatus.Published
                        // Only the newest published version of that month is in force.
                        && !db.MonthlyRosters.Any(newer => newer.DivisionId == e.Roster.DivisionId
                                                           && newer.Month == e.Roster.Month
                                                           && newer.Status == RosterStatus.Published
                                                           && newer.Version > e.Roster.Version))
            .Select(e => new
            {
                e.Date,
                e.ShiftCode,
                e.Roster.DivisionId,
                Name = e.Operator.FirstName + " " + e.Operator.Surname
            })
            .ToListAsync();

        var divisions = entries.Select(e => e.DivisionId).Distinct().ToList();
        var legends = await db.ShiftDefinitions.AsNoTracking().Where(s => divisions.Contains(s.DivisionId)).ToListAsync();

        var clashes = new List<Clash>();
        foreach (var entry in entries)
        {
            var shifts = ShiftsIn(entry.ShiftCode, legends.Where(s => s.DivisionId == entry.DivisionId));
            var reason = RosterRules.ClashReason(shifts, entry.Date, from, to, forTrip);
            if (reason is not null)
                clashes.Add(new Clash(ClashKind.Roster, entry.Name, null, reason, from, to));
        }

        return clashes;
    }

    /// <summary>
    /// Checks a roster the other way round: is anyone on a confirmed production on a day
    /// this roster says they can't work it? The App Flow Document's clash check before
    /// publishing. The roster must be loaded with its entries.
    /// </summary>
    public async Task<List<Clash>> ForRosterAsync(MonthlyRoster roster)
    {
        var monthStart = NbcTime.At(roster.Month, TimeOnly.MinValue);
        var monthEnd = NbcTime.At(roster.Month.AddMonths(1), TimeOnly.MinValue);
        var operators = roster.Entries.Select(e => e.OperatorId).Distinct().ToList();

        var work = await db.CrewAssignments
            .AsNoTracking()
            .Where(a => a.RemovedAt == null
                        && operators.Contains(a.OperatorId)
                        && (a.CrewSlot.Booking.Status == BookingStatus.Confirmed || a.CrewSlot.Booking.Status == BookingStatus.InProgress)
                        && a.CrewSlot.StartsAt < monthEnd && monthStart < a.CrewSlot.EndsAt)
            .Select(a => new
            {
                a.OperatorId,
                Name = a.Operator.FirstName + " " + a.Operator.Surname,
                a.CrewSlot.StartsAt,
                a.CrewSlot.EndsAt,
                a.CrewSlot.Booking.ProductionNo,
                a.CrewSlot.Booking.Title,
                a.CrewSlot.Booking.IsTrip
            })
            .ToListAsync();

        var legend = await db.ShiftDefinitions.AsNoTracking().Where(s => s.DivisionId == roster.DivisionId).ToListAsync();

        var clashes = new List<Clash>();
        foreach (var job in work)
        {
            foreach (var day in RosterRules.DaysCovered(job.StartsAt, job.EndsAt))
            {
                var entry = roster.Entries.FirstOrDefault(e => e.OperatorId == job.OperatorId && e.Date == day);
                if (entry is null)
                    continue;

                var reason = RosterRules.ClashReason(ShiftsIn(entry.ShiftCode, legend), day, job.StartsAt, job.EndsAt, job.IsTrip);
                if (reason is not null)
                    clashes.Add(new Clash(ClashKind.Roster, job.Name, job.ProductionNo, $"{job.Title}: {reason}", job.StartsAt, job.EndsAt));
            }
        }

        return clashes.Distinct().ToList();
    }

    /// <summary>The definitions of the codes in a cell. A code no longer on the legend is left out.</summary>
    private static List<ShiftDefinition> ShiftsIn(string cell, IEnumerable<ShiftDefinition> legend)
    {
        var codes = RosterRules.SplitCodes(cell);
        return legend.Where(s => codes.Contains(s.Code)).ToList();
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
            clashes.AddRange(await ForOperatorAsync(slot.Current!.OperatorId, slot.StartsAt, slot.EndsAt, booking.Id, booking.IsTrip));

        return clashes.Distinct().ToList();
    }
}
