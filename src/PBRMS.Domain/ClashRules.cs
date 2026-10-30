namespace PBRMS.Domain;

/// <summary>What the clash is. Some stop the work; some are warnings to acknowledge.</summary>
public enum ClashKind
{
    /// <summary>An operator is on another production at the same time.</summary>
    Operator,

    /// <summary>A studio is used by another production at the same time.</summary>
    Venue,

    /// <summary>The published roster says the operator is away.</summary>
    Roster,

    /// <summary>The operator is on leave. This can't be overridden: book someone else.</summary>
    OnLeave,

    /// <summary>The operator's studio is in use, and studio productions have priority over other work.</summary>
    StudioPriority,

    /// <summary>A warning: the operator has a day off and would be called in.</summary>
    CallIn,

    /// <summary>A warning: the operator isn't on shift at that time. Logged for HR, like a call-in.</summary>
    OutsideShift,

    /// <summary>A warning: the operator is rostered to another studio, so that studio needs cover.</summary>
    RosteredElsewhere,

    /// <summary>
    /// A warning: the operator is on an outside production, and this studio production
    /// takes precedence. Going ahead takes them off it, and its supervisors must replace them.
    /// </summary>
    TakeFromOutside,

    /// <summary>A warning: a studio production at that time is still short of crew. Studios are filled first.</summary>
    StudioNeedsCrew
}

/// <summary>
/// One clash: what is double-booked, and what it clashes with. The apps show
/// these so people know exactly what to fix (a clash is an HTTP 409 with the productions listed).
/// </summary>
/// <param name="Who">The operator's name, or the venue.</param>
/// <param name="ProductionNo">The production in the way. Empty when the clash is with the roster itself.</param>
/// <param name="Title">The production's title, or why the roster says no.</param>
/// <param name="AssignmentId">The crew assignment this is about, if there is one.</param>
public record Clash(ClashKind Kind, string Who, int? ProductionNo, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid? AssignmentId = null)
{
    /// <summary>True if this can go ahead once someone acknowledges it; false if it stops the work.</summary>
    public bool IsWarning => Kind is ClashKind.CallIn or ClashKind.OutsideShift or ClashKind.RosteredElsewhere
        or ClashKind.TakeFromOutside or ClashKind.StudioNeedsCrew;
}

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
    /// Is this one of the studios? Studio productions take precedence over other work:
    /// studios are normally filled first, and outside productions after.
    /// </summary>
    public static bool IsStudio(Venue venue) => Studios.Contains(venue);

    public static readonly IReadOnlyList<Venue> Studios = [Venue.Studio1, Venue.Studio2, Venue.Studio3, Venue.Studio4];

    /// <summary>
    /// Can a studio production take an operator off this other production? Yes, if the
    /// other one is outside the studios and hasn't started. A production already running
    /// keeps its crew, and studios can't take from each other.
    /// </summary>
    public static bool StudioTakesPrecedence(Venue wanted, Venue otherVenue, BookingStatus otherStatus) =>
        IsStudio(wanted) && !IsStudio(otherVenue) && otherStatus is not (BookingStatus.InProgress or BookingStatus.Completed);

    /// <summary>
    /// Does this go in the log of work outside the roster? Off days are more of a suggestion
    /// while there are more productions than operators, but HR wants the record of how often
    /// people work them. See <see cref="OffRosterWork"/>.
    /// </summary>
    public static bool IsOffRoster(ClashKind kind) => kind is ClashKind.CallIn or ClashKind.OutsideShift;

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
