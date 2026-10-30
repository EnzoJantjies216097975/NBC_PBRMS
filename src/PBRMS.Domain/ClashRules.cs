namespace PBRMS.Domain;

/// <summary>What is double-booked.</summary>
public enum ClashKind
{
    /// <summary>An operator is on another production at the same time.</summary>
    Operator,

    /// <summary>A studio is used by another production at the same time.</summary>
    Venue,

    /// <summary>The published roster says the operator is off, away or not on shift.</summary>
    Roster
}

/// <summary>
/// One clash: what is double-booked, and what it clashes with. The apps show
/// these so people know exactly what to fix (a clash is an HTTP 409 with the productions listed).
/// </summary>
/// <param name="Who">The operator's name, or the venue.</param>
/// <param name="ProductionNo">The production in the way. Empty when the clash is with the roster itself.</param>
/// <param name="Title">The production's title, or why the roster says no.</param>
public record Clash(ClashKind Kind, string Who, int? ProductionNo, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// What counts as a clash (BR-06). The rules are here with no database code; the
/// queries that apply them are in PBRMS.Data, ClashChecker.
/// </summary>
/// <remarks>Equipment bookings join this check when they are built (TR-01). Roster rules are in <see cref="RosterRules"/>.</remarks>
public static class ClashRules
{
    /// <summary>
    /// Do two periods overlap? One ending exactly when the other starts doesn't count,
    /// so an operator can go straight from one production to the next.
    /// </summary>
    public static bool Overlaps(DateTimeOffset startA, DateTimeOffset endA, DateTimeOffset startB, DateTimeOffset endB) =>
        startA < endB && startB < endA;

    /// <summary>
    /// Can two productions clash over this venue? Only the rooms NBC has one of. Location
    /// shoots, OBs and the like are in different places even though they share a venue type
    /// (the board's rule: "On location" never clashes).
    /// </summary>
    public static bool VenueCanClash(Venue venue) =>
        venue is Venue.Studio1 or Venue.Studio2 or Venue.Studio3 or Venue.Studio4 or Venue.App;

    /// <summary>
    /// Bookings whose crew count as taken: everything except drafts and cancelled bookings
    /// (Backend Schema Document, "Conflict detection").
    /// </summary>
    public static readonly IReadOnlyList<BookingStatus> StatusesHoldingCrew =
        Enum.GetValues<BookingStatus>().Where(s => s is not (BookingStatus.Draft or BookingStatus.Cancelled)).ToArray();

    /// <summary>
    /// Bookings whose studio counts as taken: only those that got as far as the Booking
    /// Officer. An earlier booking may yet change or never be approved, and the Booking
    /// Officer can't move it, so it must not block a booking that is ready to confirm.
    /// </summary>
    public static readonly IReadOnlyList<BookingStatus> StatusesHoldingVenue =
    [
        BookingStatus.Finalisation, BookingStatus.Confirmed, BookingStatus.InProgress, BookingStatus.Completed
    ];
}
