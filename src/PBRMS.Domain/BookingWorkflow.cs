namespace PBRMS.Domain;

/// <summary>Something a person can do to a booking.</summary>
public enum BookingAction
{
    /// <summary>The producer hands in their draft.</summary>
    Submit,

    /// <summary>Pass it on to the next stage.</summary>
    Approve,

    /// <summary>
    /// Send it back with a reason so it can be fixed. The App Flow Document has
    /// different words for this at each stage (the EP "returns", a
    /// supervisor "declines"), but it is the same action.
    /// </summary>
    SendBack,

    /// <summary>The producer confirms the production has started.</summary>
    Start,

    /// <summary>The producer confirms the production has ended.</summary>
    End,

    /// <summary>Call the booking off. Possible from any stage until it is completed.</summary>
    Cancel
}

/// <summary>
/// Who may move a booking where. All the approval rules live here, with no database
/// or web code, so they can be read in one place and tested on their own.
/// Source: App Flow Document, "Booking approval flow" and "Booking states".
/// </summary>
public static class BookingWorkflow
{
    /// <summary>One allowed move: in status From, someone holding role ActedBy may do Action, and the booking goes to To.</summary>
    public record Step(BookingStatus From, BookingAction Action, Role ActedBy, BookingStatus To);

    /// <summary>
    /// Every allowed move (Cancel is handled separately). A move that isn't listed
    /// isn't allowed.
    /// Sending back: the EP sends it to the producer to fix (Draft); whoever is crewing
    /// it sends it back to the EP (App Flow Document, BR-07).
    /// </summary>
    /// <remarks>
    /// Leaving crew assignment isn't listed because nobody presses a button for it: when the
    /// last crew slot is filled, a studio booking moves on to the Booking Officer to confirm,
    /// and an outside booking is confirmed there and then, because the supervisors phone their
    /// operators before deciding (see CrewProgress). Confirming also needs the clash check
    /// (TR-02); see the transition endpoint.
    /// </remarks>
    public static readonly IReadOnlyList<Step> Steps =
    [
        new(BookingStatus.Draft, BookingAction.Submit, Role.Producer, BookingStatus.EpReview),

        new(BookingStatus.EpReview, BookingAction.Approve, Role.ExecutiveProducer, BookingStatus.CrewAssignment),
        new(BookingStatus.EpReview, BookingAction.SendBack, Role.ExecutiveProducer, BookingStatus.Draft),

        new(BookingStatus.CrewAssignment, BookingAction.SendBack, Role.Supervisor, BookingStatus.EpReview),

        new(BookingStatus.Finalisation, BookingAction.Approve, Role.BookingOfficer, BookingStatus.Confirmed),

        new(BookingStatus.Confirmed, BookingAction.Start, Role.Producer, BookingStatus.InProgress),
        new(BookingStatus.InProgress, BookingAction.End, Role.Producer, BookingStatus.Completed),
    ];

    /// <summary>
    /// How far past its booked end a production can run before it counts as overtime and
    /// supervisors and managers are told (TR-07: "30 min overrun").
    /// </summary>
    public static readonly TimeSpan OverrunThreshold = TimeSpan.FromMinutes(30);

    /// <summary>Besides the booking's own producer, these roles may cancel a booking.</summary>
    public static readonly IReadOnlyList<Role> CancelledBy =
        [Role.ExecutiveProducer, Role.BookingOfficer, Role.Admin];

    /// <summary>The step for doing this action in this status, or null if it isn't allowed then.</summary>
    public static Step? FindStep(BookingStatus from, BookingAction action) =>
        Steps.FirstOrDefault(s => s.From == from && s.Action == action);

    /// <summary>Sending back and cancelling must say why, so the producer knows what to fix (E1-02).</summary>
    public static bool NeedsNote(BookingAction action) => action is BookingAction.SendBack or BookingAction.Cancel;

    /// <summary>A booking can be cancelled at any stage until it has finished (or is already cancelled).</summary>
    public static bool CanBeCancelled(BookingStatus status) =>
        status is not (BookingStatus.Completed or BookingStatus.Cancelled);

    /// <summary>
    /// May this user take this step on this booking? Holding the role is not enough;
    /// it has to be for this booking:
    /// - Producer: only the booking's own producer.
    /// - Supervisor: whoever may fill the booking's crew slots (see <see cref="MayStaff"/>).
    /// - Executive Producer: of the booking's department, or of a
    ///   department above it (e.g. Content Hub over its sub-departments).
    /// A role held NBC-wide (no unit) covers every booking.
    /// </summary>
    /// <param name="departmentChain">The booking's department and every unit above it (see <see cref="OrgUnit.ChainUp"/>).</param>
    public static bool MayTake(Step step, Booking booking, Guid userId, IEnumerable<RoleAssignment> userRoles, ISet<Guid> departmentChain, DateTimeOffset now) =>
        step.ActedBy switch
        {
            Role.Producer => booking.ProducerId == userId,
            Role.Supervisor => booking.CrewSlots.Any(s => MayStaff(booking, s.DivisionId, userRoles, now)),
            _ => HoldsRoleIn(userRoles, step.ActedBy, departmentChain)
        };

    /// <summary>May this user cancel this booking? Its producer, the EP of its department, the Booking Officer or an Admin.</summary>
    public static bool MayCancel(Booking booking, Guid userId, IEnumerable<RoleAssignment> userRoles, ISet<Guid> departmentChain) =>
        booking.ProducerId == userId
        || CancelledBy.Any(role => HoldsRoleIn(userRoles, role, departmentChain));

    /// <summary>
    /// Who decides the crew. The Booking Officer books people in the studios. Supervisors
    /// decide who goes outside and on trips, each for their own division.
    /// </summary>
    public static Role CrewLead(Venue venue) => ClashRules.IsStudio(venue) ? Role.BookingOfficer : Role.Supervisor;

    /// <summary>
    /// The latest a supervisor has to crew an outside production: when the daily roster
    /// for its day is built, at 16h00 the day before (TRD, "Daily roster generation").
    /// </summary>
    public static DateTimeOffset CrewDeadline(DateTimeOffset startsAt) =>
        NbcTime.At(NbcTime.DateOf(startsAt).AddDays(-1), DailyRosterTime);

    /// <summary>
    /// NBC has one APP room, so there is one way to book it: an APP booking (see
    /// <see cref="AppSession"/>). A normal booking in APP couldn't see those, and the room
    /// would be double-booked. So <see cref="Venue.App"/> is refused on the booking form.
    /// </summary>
    public const string AppIsBookedDirectly = "APP isn't booked here. Book it directly with the APP operator.";

    /// <summary>When tomorrow's daily roster is built (TRD, "Daily roster generation").</summary>
    public static readonly TimeOnly DailyRosterTime = new(16, 0);

    /// <summary>
    /// May this user put operators on a slot of this division on this booking? The division's
    /// supervisor always may, on studio crew as needed too. The Booking Officer may on any
    /// studio production, and on outside ones once the daily roster is due, to decide
    /// whatever is still undecided.
    /// </summary>
    public static bool MayStaff(Booking booking, Guid divisionId, IEnumerable<RoleAssignment> userRoles, DateTimeOffset now)
    {
        var division = new HashSet<Guid> { divisionId };

        if (HoldsRoleIn(userRoles, Role.Supervisor, division))
            return true;

        var bookingOfficerMay = CrewLead(booking.Venue) == Role.BookingOfficer || now >= CrewDeadline(booking.StartsAt);
        return bookingOfficerMay && HoldsRoleIn(userRoles, Role.BookingOfficer, division);
    }

    /// <summary>
    /// Is this booking's crew in this user's queue? Supervisors may change studio crew, but
    /// they only need to see outside work: studio bookings queue for the Booking Officer.
    /// </summary>
    public static bool IsInCrewQueueOf(Booking booking, IEnumerable<RoleAssignment> userRoles, DateTimeOffset now) =>
        CrewLead(booking.Venue) == Role.BookingOfficer
            ? userRoles.Any(r => r.Role == Role.BookingOfficer)
            : booking.CrewSlots.Any(s => MayStaff(booking, s.DivisionId, userRoles, now));

    /// <summary>Does the user hold this role in one of these units, or NBC-wide?</summary>
    public static bool HoldsRoleIn(IEnumerable<RoleAssignment> userRoles, Role role, ISet<Guid> units) =>
        userRoles.Any(r => r.Role == role && (r.OrgUnitId is null || units.Contains(r.OrgUnitId.Value)));
}
