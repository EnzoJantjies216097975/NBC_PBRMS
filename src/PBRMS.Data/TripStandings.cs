using Microsoft.EntityFrameworkCore;
using PBRMS.Domain;

namespace PBRMS.Data;

/// <summary>
/// Reads the trip rotation from the database. Who is owed the next trip is decided in
/// <see cref="TripRotation"/>; this gathers what that needs, and says who a choice passes over.
/// </summary>
public class TripStandings(PbrmsDbContext db, ClashChecker clashChecker)
{
    /// <summary>A division's rotation, next in turn first. Operators kept to the building aren't in it (BR-09).</summary>
    public async Task<List<TripStanding>> ForDivisionAsync(Guid divisionId)
    {
        var today = NbcTime.DateOf(DateTimeOffset.UtcNow);

        var people = await db.RoleAssignments
            .AsNoTracking()
            .Where(r => r.OrgUnitId == divisionId && r.Role == Role.Operator && r.User.IsActive && !r.StudioBound)
            .Select(r => new
            {
                r.UserId,
                Name = r.User.FirstName + " " + r.User.Surname,
                r.TripSitOutReason,
                r.TripSitOutUntil
            })
            .ToListAsync();
        var ids = people.Select(p => p.UserId).ToList();

        var booked = await db.CrewAssignments
            .AsNoTracking()
            .Where(a => ids.Contains(a.OperatorId)
                        && a.RemovedAt == null
                        && a.CrewSlot.Booking.IsTrip
                        && ClashRules.StatusesHoldingCrew.Contains(a.CrewSlot.Booking.Status))
            .Select(a => new
            {
                a.OperatorId,
                a.CrewSlot.BookingId,
                a.CrewSlot.Booking.Title,
                a.CrewSlot.Booking.StartsAt,
                a.CrewSlot.Booking.EndsAt,
                a.CrewSlot.Booking.Status
            })
            .ToListAsync();

        var logged = await db.LoggedTrips
            .AsNoTracking()
            .Where(t => ids.Contains(t.OperatorId) && t.RemovedAt == null)
            .ToListAsync();

        // Two positions on one trip are still one trip.
        var bookedLines = booked
            .DistinctBy(b => (b.OperatorId, b.BookingId))
            .Where(b => TripRotation.Counts(b.Status, NbcTime.DateOf(b.EndsAt), today))
            .Select(b => (b.OperatorId, Line: new TripLine(null, b.Title, NbcTime.DateOf(b.StartsAt), NbcTime.DateOf(b.EndsAt))));
        var loggedLines = logged.Select(t => (t.OperatorId, Line: new TripLine(t.Id, t.Title, t.From, t.To)));
        var trips = bookedLines.Concat(loggedLines).ToLookup(t => t.OperatorId, t => t.Line);

        return TripRotation.Rank(
            people.Select(p => new TripOperator(p.UserId, p.Name, p.TripSitOutReason, p.TripSitOutUntil, trips[p.UserId].ToList())),
            today);
    }

    /// <summary>
    /// Who this choice passes over: people ahead in the queue who could have gone. Someone on
    /// leave or on another production at that time couldn't have, so nobody has to explain
    /// skipping them. A day off doesn't excuse them, because off days can be worked (BR-10).
    /// </summary>
    /// <param name="chosenNow">Everyone being put on this booking in the same save, who are going anyway.</param>
    public async Task<List<Clash>> PassedOverAsync(Booking booking, CrewSlot slot, Guid chosen, IReadOnlyCollection<Guid> chosenNow)
    {
        var rotation = await ForDivisionAsync(slot.DivisionId);
        var chosenPlace = rotation.FirstOrDefault(s => s.OperatorId == chosen)?.Place ?? int.MaxValue;
        var passedOver = new List<Clash>();

        foreach (var person in rotation.Where(s => s.Place < chosenPlace && !chosenNow.Contains(s.OperatorId)))
        {
            var clashes = await clashChecker.ForOperatorAsync(person.OperatorId, slot.StartsAt, slot.EndsAt, booking);
            if (clashes.All(c => c.IsWarning))
                passedOver.Add(new Clash(ClashKind.TripTurn, person.Name, null, $"turn {person.Place}, {person.Trips} trips so far", slot.StartsAt, slot.EndsAt));
        }

        return passedOver;
    }
}
