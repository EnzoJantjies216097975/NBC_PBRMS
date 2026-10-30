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
    /// Why this operator can't work the given period, or needs a warning first: other
    /// productions and APP sessions they are on, and what the published roster says.
    /// </summary>
    /// <param name="forBooking">
    /// The booking the period is for, if there is one. It tells us where the work is, whether
    /// it's a trip, and to ignore this booking itself, because an operator may hold two
    /// positions on the same production.
    /// </param>
    public async Task<List<Clash>> ForOperatorAsync(Guid operatorId, DateTimeOffset from, DateTimeOffset to, Booking? forBooking = null)
    {
        var clashes = await OtherProductionsAsync(operatorId, from, to, forBooking);
        clashes.AddRange(await AppSessionsAsync(operatorId, from, to));
        clashes.AddRange(await RosterClashesAsync(operatorId, from, to, forBooking));
        return clashes;
    }

    /// <summary>
    /// The other productions this operator is on. A studio production takes precedence, so
    /// for a studio booking, an outside production is a warning rather than a clash.
    /// </summary>
    private async Task<List<Clash>> OtherProductionsAsync(Guid operatorId, DateTimeOffset from, DateTimeOffset to, Booking? forBooking)
    {
        var exceptBookingId = forBooking?.Id;

        var jobs = await db.CrewAssignments
            .AsNoTracking()
            .Where(a => a.OperatorId == operatorId
                        && a.RemovedAt == null
                        && a.CrewSlot.BookingId != exceptBookingId
                        && ClashRules.StatusesHoldingCrew.Contains(a.CrewSlot.Booking.Status)
                        // Same as ClashRules.Overlaps, written out so SQL Server can run it.
                        && a.CrewSlot.StartsAt < to && from < a.CrewSlot.EndsAt)
            .Select(a => new
            {
                a.Id,
                Name = a.Operator.FirstName + " " + a.Operator.Surname,
                a.CrewSlot.Booking.ProductionNo,
                a.CrewSlot.Booking.Title,
                a.CrewSlot.StartsAt,
                a.CrewSlot.EndsAt,
                a.CrewSlot.Booking.Venue,
                a.CrewSlot.Booking.Status
            })
            .ToListAsync();

        return jobs
            .Select(job =>
            {
                var studioWins = forBooking is not null && ClashRules.StudioTakesPrecedence(forBooking.Venue, job.Venue, job.Status);
                var kind = studioWins ? ClashKind.TakeFromOutside : ClashKind.Operator;
                return new Clash(kind, job.Name, job.ProductionNo, job.Title, job.StartsAt, job.EndsAt, job.Id);
            })
            .ToList();
    }

    /// <summary>Confirmed APP sessions: the operator is busy then, like on a production.</summary>
    private async Task<List<Clash>> AppSessionsAsync(Guid operatorId, DateTimeOffset from, DateTimeOffset to) =>
        await db.AppSessions
            .AsNoTracking()
            .Where(s => s.OperatorId == operatorId
                        && s.Status == AppSessionStatus.Confirmed
                        // Same as ClashRules.Overlaps, written out so SQL Server can run it.
                        && s.StartsAt < to && from < s.EndsAt)
            .Select(s => new Clash(ClashKind.Operator, s.Operator.FirstName + " " + s.Operator.Surname, null, "APP session: " + s.Production, s.StartsAt, s.EndsAt, null))
            .ToListAsync();

    /// <summary>
    /// Studios are filled first. When crewing an outside production, this lists studio
    /// productions at that time still waiting for someone from the same division.
    /// </summary>
    public async Task<List<Clash>> StudiosNeedingCrewAsync(Booking booking, CrewSlot slot)
    {
        if (ClashRules.IsStudio(booking.Venue))
            return [];

        return await db.CrewSlots
            .AsNoTracking()
            .Where(s => s.DivisionId == slot.DivisionId
                        && s.Booking.Status == BookingStatus.CrewAssignment
                        && ClashRules.Studios.Contains(s.Booking.Venue)
                        && !s.Assignments.Any(a => a.RemovedAt == null)
                        // Same as ClashRules.Overlaps, written out so SQL Server can run it.
                        && s.StartsAt < slot.EndsAt && slot.StartsAt < s.EndsAt)
            .Select(s => new Clash(ClashKind.StudioNeedsCrew, s.PositionLabel, s.Booking.ProductionNo, s.Booking.Title, s.StartsAt, s.EndsAt, null))
            .ToListAsync();
    }

    /// <summary>
    /// What the rosters in force say about this period. Only a published roster counts:
    /// a draft may still change, and a blank cell (no entry) is unknown, not a clash.
    /// </summary>
    private async Task<List<Clash>> RosterClashesAsync(Guid operatorId, DateTimeOffset from, DateTimeOffset to, Booking? forBooking)
    {
        var days = RosterRules.DaysCovered(from, to);
        var (firstDay, lastDay) = (days[0], days[^1]);

        var entries = await EntriesInForce()
            .AsNoTracking()
            .Where(e => e.OperatorId == operatorId && e.Date >= firstDay && e.Date <= lastDay)
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
            var finding = RosterRules.Check(shifts, entry.Date, from, to, forBooking?.IsTrip ?? false, forBooking?.Venue);
            if (finding is null)
                continue;

            var clash = new Clash(finding.Kind, entry.Name, null, finding.Reason, from, to);

            // Studio productions have priority: someone rostered to a studio that's in use
            // can't be taken for work outside the studios.
            if (finding.Kind == ClashKind.RosteredElsewhere
                && !ClashRules.IsStudio(forBooking!.Venue)
                && await StudioInUseAsync(finding.Studio!.Value, from, to, forBooking.Id))
            {
                clash = clash with { Kind = ClashKind.StudioPriority, Title = $"{finding.Reason}, which is in use" };
            }

            clashes.Add(clash);
        }

        return clashes;
    }

    private Task<bool> StudioInUseAsync(Venue studio, DateTimeOffset from, DateTimeOffset to, Guid exceptBookingId) =>
        db.Bookings.AnyAsync(b => b.Id != exceptBookingId
                                  && b.Venue == studio
                                  && ClashRules.StatusesHoldingCrew.Contains(b.Status)
                                  // Same as ClashRules.Overlaps, written out so SQL Server can run it.
                                  && b.StartsAt < to && from < b.EndsAt);

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
                a.Id,
                a.OperatorId,
                Name = a.Operator.FirstName + " " + a.Operator.Surname,
                a.CrewSlot.StartsAt,
                a.CrewSlot.EndsAt,
                a.CrewSlot.Booking.ProductionNo,
                a.CrewSlot.Booking.Title,
                a.CrewSlot.Booking.IsTrip,
                a.CrewSlot.Booking.Venue
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

                var finding = RosterRules.Check(ShiftsIn(entry.ShiftCode, legend), day, job.StartsAt, job.EndsAt, job.IsTrip, job.Venue);
                if (finding is not null)
                    clashes.Add(new Clash(finding.Kind, job.Name, job.ProductionNo, $"{job.Title}: {finding.Reason}", job.StartsAt, job.EndsAt, job.Id));
            }
        }

        return clashes.Distinct().ToList();
    }

    /// <summary>Cells of the rosters in force: published, and only the newest published version of that month.</summary>
    public IQueryable<RosterEntry> EntriesInForce() =>
        db.RosterEntries.Where(e => e.Roster.Status == RosterStatus.Published
                                    && !db.MonthlyRosters.Any(newer => newer.DivisionId == e.Roster.DivisionId
                                                                       && newer.Month == e.Roster.Month
                                                                       && newer.Status == RosterStatus.Published
                                                                       && newer.Version > e.Roster.Version));

    /// <summary>The definitions of the codes in a cell. A code no longer on the legend is left out.</summary>
    public static List<ShiftDefinition> ShiftsIn(string cell, IEnumerable<ShiftDefinition> legend)
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
            .Select(b => new Clash(ClashKind.Venue, booking.Venue.ToString(), b.ProductionNo, b.Title, b.StartsAt, b.EndsAt, null))
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
        {
            var current = slot.Current!;
            var found = await ForOperatorAsync(current.OperatorId, slot.StartsAt, slot.EndsAt, booking);

            // Roster findings are about this assignment; other productions point to their own.
            clashes.AddRange(found.Select(c => ClashRules.IsOffRoster(c.Kind) ? c with { AssignmentId = current.Id } : c));
        }

        return clashes.Distinct().ToList();
    }
}
