using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Builds the sheet for one day: confirmed bookings, then whoever the monthly roster has on
/// duty but not on a production, then the Booking Officer's changes on top (see <see cref="DailyRoster"/>).
/// </summary>
public class DailyRosterComposer(PbrmsDbContext db, ClashChecker clashChecker)
{
    private static readonly BookingStatus[] OnTheSheet = [BookingStatus.Confirmed, BookingStatus.InProgress, BookingStatus.Completed];
    private static readonly BookingStatus[] StillBeingDecided = [BookingStatus.CrewAssignment, BookingStatus.Finalisation];

    public async Task<DailyRosterView> ComposeAsync(DateOnly date)
    {
        var (dayStart, dayEnd) = (NbcTime.At(date, TimeOnly.MinValue), NbcTime.At(date.AddDays(1), TimeOnly.MinValue));

        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.Producer)
            .Include(b => b.CrewSlots).ThenInclude(s => s.Division)
            .Include(b => b.CrewSlots).ThenInclude(s => s.Assignments.Where(a => a.RemovedAt == null)).ThenInclude(a => a.Operator)
            .Where(b => b.StartsAt < dayEnd && dayStart < b.EndsAt
                        && (OnTheSheet.Contains(b.Status) || StillBeingDecided.Contains(b.Status)))
            .OrderBy(b => b.StartsAt)
            .AsSplitQuery()
            .ToListAsync();

        var confirmed = bookings.Where(b => OnTheSheet.Contains(b.Status)).ToList();

        var lines = BookingLines(confirmed);
        lines.AddRange(await RosteredLinesAsync(date, confirmed));
        lines.AddRange(await AppLinesAsync(dayStart, dayEnd));

        var saved = await db.DailyRosters.AsNoTracking()
            .Include(r => r.Lines).ThenInclude(l => l.Crew)
            .FirstOrDefaultAsync(r => r.Date == date);

        lines = ApplyEdits(lines, saved);
        AddEmptyStudios(lines);

        return new DailyRosterView(
            date,
            saved?.Announcement,
            lines.OrderBy(l => DailyRosterRules.Rank(l.Area)).ThenBy(l => l.Area).ThenBy(l => l.StartsAt).ToList(),
            bookings.Where(b => StillBeingDecided.Contains(b.Status)).Select(Undecided).ToList());
    }

    /// <summary>A row per confirmed booking. Rows such as "Production 1" are numbered in start-time order.</summary>
    private static List<DailyLine> BookingLines(List<Booking> confirmed)
    {
        var counts = new Dictionary<string, int>();
        var lines = new List<DailyLine>();

        foreach (var booking in confirmed)
        {
            var kind = booking.IsTrip ? "Trip" : booking.Venue.ToString();
            counts[kind] = counts.GetValueOrDefault(kind) + 1;

            var producer = $"{booking.Producer.FirstName} {booking.Producer.Surname}";
            var phone = booking.Producer.Phone is null ? "" : $", {booking.Producer.Phone}";
            var where = string.IsNullOrEmpty(booking.VenueText) ? "" : $"{booking.VenueText}. ";

            lines.Add(new DailyLine(
                null,
                booking.Id,
                DailyRosterRules.AreaOf(booking, counts[kind]),
                booking.StartsAt,
                booking.EndsAt,
                $"#{booking.ProductionNo} {booking.Title}",
                $"{where}{producer}{phone}",
                booking.CrewSlots
                    .Where(s => s.Current is not null)
                    .Select(s => new DailyCrewMember(s.Division.Name, $"{s.Current!.Operator.FirstName} {s.Current.Operator.Surname}"))
                    .ToList()));
        }

        return lines;
    }

    /// <summary>
    /// Whoever the monthly roster has working that day but isn't on a production. People
    /// rostered to a studio show on its row, and the rest under "On shift" or "Away".
    /// Days off and leave don't appear: this sheet is who's in the building.
    /// </summary>
    private async Task<List<DailyLine>> RosteredLinesAsync(DateOnly date, List<Booking> confirmed)
    {
        var busy = confirmed
            .SelectMany(b => b.CrewSlots)
            .Where(s => s.Current is not null)
            .Select(s => s.Current!.OperatorId)
            .ToHashSet();

        var cells = await clashChecker.EntriesInForce()
            .AsNoTracking()
            .Where(e => e.Date == date)
            .Select(e => new
            {
                e.OperatorId,
                e.ShiftCode,
                e.Roster.DivisionId,
                Division = e.Roster.Division.Name,
                Name = e.Operator.FirstName + " " + e.Operator.Surname
            })
            .ToListAsync();

        var divisions = cells.Select(c => c.DivisionId).Distinct().ToList();
        var legends = await db.ShiftDefinitions.AsNoTracking().Where(s => divisions.Contains(s.DivisionId)).ToListAsync();

        var rows = new List<(string Area, string Title, DateTimeOffset? Start, DateTimeOffset? End, DailyCrewMember Member)>();

        foreach (var cell in cells.Where(c => !busy.Contains(c.OperatorId)))
        {
            var shift = ClashChecker
                .ShiftsIn(cell.ShiftCode, legends.Where(s => s.DivisionId == cell.DivisionId))
                .FirstOrDefault(s => s.Kind is ShiftKind.Worked or ShiftKind.Away);
            if (shift is null)
                continue;

            var window = shift.WindowsOn(date).Select(w => ((DateTimeOffset, DateTimeOffset)?)w).FirstOrDefault();
            var (area, title) = shift switch
            {
                { Studio: { } studio } => (DailyRosterRules.StudioName(studio), DailyRosterRules.OnDutyTitle),
                { Kind: ShiftKind.Away } => (DailyRosterRules.AwayArea, $"{shift.Code}: {shift.Label}"),
                _ => (DailyRosterRules.OnShiftArea, $"{shift.Code}: {shift.Label}")
            };

            rows.Add((area, title, window?.Item1, window?.Item2, new DailyCrewMember(cell.Division, cell.Name)));
        }

        return rows
            .GroupBy(r => (r.Area, r.Title, r.Start, r.End))
            .Select(g => new DailyLine(null, null, g.Key.Area, g.Key.Start, g.Key.End, g.Key.Title, null, g.Select(r => r.Member).ToList(), Rostered: true))
            .ToList();
    }

    /// <summary>A row per confirmed APP session, so the sheet shows the operator is taken.</summary>
    private async Task<List<DailyLine>> AppLinesAsync(DateTimeOffset dayStart, DateTimeOffset dayEnd) =>
        await db.AppSessions.AsNoTracking()
            .Where(s => s.Status == AppSessionStatus.Confirmed && s.StartsAt < dayEnd && dayStart < s.EndsAt)
            .Select(s => new DailyLine(
                null, null, "APP", s.StartsAt, s.EndsAt, s.Production,
                s.Requester.FirstName + " " + s.Requester.Surname,
                new List<DailyCrewMember> { new("APP", s.Operator.FirstName + " " + s.Operator.Surname) },
                false))
            .ToListAsync();

    /// <summary>Lays the Booking Officer's changes over the generated rows.</summary>
    private static List<DailyLine> ApplyEdits(List<DailyLine> lines, DailyRoster? saved)
    {
        if (saved is null)
            return lines;

        var replaced = saved.Lines.Where(l => l.BookingId is not null).Select(l => l.BookingId).ToHashSet();
        var kept = lines.Where(l => l.BookingId is null || !replaced.Contains(l.BookingId)).ToList();

        kept.AddRange(saved.Lines
            .Where(l => !l.Hidden)
            .Select(l => new DailyLine(
                l.Id, l.BookingId, l.Area, l.StartsAt, l.EndsAt, l.Title, l.Detail,
                l.Crew.Select(c => new DailyCrewMember(c.Division, c.Name)).ToList())));

        return kept;
    }

    /// <summary>A studio with nothing in it still gets a row that says so (the board's rule).</summary>
    private static void AddEmptyStudios(List<DailyLine> lines)
    {
        foreach (var studio in ClashRules.Studios.Select(DailyRosterRules.StudioName))
        {
            if (lines.All(l => l.Area != studio))
                lines.Add(new DailyLine(null, null, studio, null, null, DailyRosterRules.NobodyOn, null, []));
        }
    }

    private static UndecidedBooking Undecided(Booking booking) => new(
        booking.Id,
        booking.ProductionNo,
        booking.Title,
        booking.StartsAt,
        booking.Status,
        booking.CrewSlots.Where(s => s.Current is null).Select(s => s.PositionLabel).ToList());
}
