namespace PBRMS.Domain;

/// <summary>
/// Why an operator is sitting out the trip rotation. A short list rather than free text,
/// so nobody has to write personal details down to ask for this (SRS FR-TRP-03).
/// </summary>
public enum TripSitOutReason
{
    Family,
    School,
    Studies,
    Health,
    Other
}

/// <summary>One trip an operator has had or is booked on.</summary>
/// <param name="LoggedId">Set on a trip logged by hand, which is what makes it removable. Empty for a booking.</param>
public record TripLine(Guid? LoggedId, string Title, DateOnly From, DateOnly To);

/// <summary>What the rotation needs to know about one operator.</summary>
public record TripOperator(Guid Id, string Name, TripSitOutReason? SitOutReason, DateOnly? SitOutUntil, List<TripLine> Trips);

/// <summary>An operator's place in their division's trip rotation.</summary>
/// <param name="Place">Their turn, 1 being next. Empty if they're out of the queue: already on a trip, or sitting out.</param>
/// <param name="Trips">Every trip they've had or are booked on.</param>
/// <param name="LastBack">The day their last finished trip ended. Empty if they've never been out.</param>
/// <param name="NextTrip">The day their next trip starts, if they're on one.</param>
/// <param name="History">Their trips, newest first.</param>
public record TripStanding(
    Guid OperatorId, string Name, int? Place, int Trips, DateOnly? LastBack, DateOnly? NextTrip,
    TripSitOutReason? SittingOut, DateOnly? SittingOutUntil, List<TripLine> History);

/// <summary>A supervisor takes an operator out of the rotation, or puts them back with no reason.</summary>
/// <param name="Until">The last day they sit out. Empty means until they're put back.</param>
public record TripSitOutForm(TripSitOutReason? Reason, DateOnly? Until);

/// <summary>A trip from before PBRMS, or one nobody was crewed on here (SRS FR-TRP-01).</summary>
public record LoggedTripForm(string? Title, DateOnly From, DateOnly To);

/// <summary>
/// Trips are worth money and time away, so they go round in turn (BR-10, SRS FR-TRP-01 to 03).
/// The rules are here with no database code; the queries are in PBRMS.Data, TripStandings.
/// Operators kept to the building never appear (BR-09).
/// </summary>
public static class TripRotation
{
    /// <summary>
    /// Does this booked trip count towards the operator's total? One that hasn't ended counts
    /// as soon as they're on the crew, or the same person could be picked twice. One that has
    /// ended counts only if it was confirmed: an unfinished booking left behind never happened.
    /// </summary>
    public static bool Counts(BookingStatus status, DateOnly lastDay, DateOnly today) =>
        lastDay >= today || status is BookingStatus.Confirmed or BookingStatus.InProgress or BookingStatus.Completed;

    /// <summary>
    /// The queue, next in turn first. Fewest trips goes first, then whoever has never been
    /// out, then whoever has waited longest, then by name so the order never wobbles.
    /// Anyone already on a trip still to come, or sitting out, follows with no place.
    /// </summary>
    public static List<TripStanding> Rank(IEnumerable<TripOperator> operators, DateOnly today)
    {
        var place = 0;

        return operators
            .Select(o => Stand(o, today))
            .OrderBy(s => s.Trips)
            .ThenBy(s => s.LastBack ?? DateOnly.MinValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .Select(s => s.NextTrip is null && s.SittingOut is null ? s with { Place = ++place } : s)
            .ToList()
            .OrderBy(s => s.Place is null) // keeps the order above within each group
            .ToList();
    }

    private static TripStanding Stand(TripOperator person, DateOnly today)
    {
        var finished = person.Trips.Where(t => t.To < today).ToList();
        var ahead = person.Trips.Where(t => t.To >= today).ToList();

        // A sit-out lapses by itself after its last day, and it's beside the point once
        // they're actually crewed on a trip: what's booked outranks what was asked for.
        var sittingOut = person.SitOutReason is not null
                         && (person.SitOutUntil is null || person.SitOutUntil >= today)
                         && ahead.Count == 0;

        return new TripStanding(
            person.Id,
            person.Name,
            null,
            person.Trips.Count,
            finished.Count > 0 ? finished.Max(t => t.To) : null,
            ahead.Count > 0 ? ahead.Min(t => t.From) : null,
            sittingOut ? person.SitOutReason : null,
            sittingOut ? person.SitOutUntil : null,
            person.Trips.OrderByDescending(t => t.From).ToList());
    }
}
