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
    /// different words for this at each stage (the PM "denies", the EP "returns", a
    /// supervisor "declines"), but it is the same action.
    /// </summary>
    SendBack,

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
    /// Sending back: the PM and EP send it to the producer to fix (Draft); a
    /// supervisor sends it back to the EP (App Flow Document, BR-07).
    /// </summary>
    /// <remarks>
    /// Crew assignment → Finalisation → Confirmed come with the crew-assignment work,
    /// because confirming needs the clash check (TR-02).
    /// </remarks>
    public static readonly IReadOnlyList<Step> Steps =
    [
        new(BookingStatus.Draft, BookingAction.Submit, Role.Producer, BookingStatus.PmReview),

        new(BookingStatus.PmReview, BookingAction.Approve, Role.ProductionManager, BookingStatus.EpReview),
        new(BookingStatus.PmReview, BookingAction.SendBack, Role.ProductionManager, BookingStatus.Draft),

        new(BookingStatus.EpReview, BookingAction.Approve, Role.ExecutiveProducer, BookingStatus.CrewAssignment),
        new(BookingStatus.EpReview, BookingAction.SendBack, Role.ExecutiveProducer, BookingStatus.Draft),

        new(BookingStatus.CrewAssignment, BookingAction.SendBack, Role.Supervisor, BookingStatus.EpReview),
    ];

    /// <summary>Besides the booking's own producer, these roles may cancel a booking.</summary>
    public static readonly IReadOnlyList<Role> CancelledBy =
        [Role.ProductionManager, Role.ExecutiveProducer, Role.BookingOfficer, Role.Admin];

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
    /// - Supervisor: supervisor of a division named in the booking's crew slots.
    /// - Production Manager, Executive Producer: of the booking's department, or of a
    ///   department above it (e.g. Content Hub over its sub-departments).
    /// A role held NBC-wide (no unit) covers every booking.
    /// </summary>
    /// <param name="departmentChain">The booking's department and every unit above it (see <see cref="OrgUnit.ChainUp"/>).</param>
    public static bool MayTake(Step step, Booking booking, Guid userId, IEnumerable<RoleAssignment> userRoles, ISet<Guid> departmentChain) =>
        step.ActedBy switch
        {
            Role.Producer => booking.ProducerId == userId,
            Role.Supervisor => HoldsRoleIn(userRoles, Role.Supervisor, booking.CrewSlots.Select(s => s.DivisionId).ToHashSet()),
            _ => HoldsRoleIn(userRoles, step.ActedBy, departmentChain)
        };

    /// <summary>May this user cancel this booking? Its producer, the PM or EP of its department, the Booking Officer or an Admin.</summary>
    public static bool MayCancel(Booking booking, Guid userId, IEnumerable<RoleAssignment> userRoles, ISet<Guid> departmentChain) =>
        booking.ProducerId == userId
        || CancelledBy.Any(role => HoldsRoleIn(userRoles, role, departmentChain));

    /// <summary>Does the user hold this role in one of these units, or NBC-wide?</summary>
    public static bool HoldsRoleIn(IEnumerable<RoleAssignment> userRoles, Role role, ISet<Guid> units) =>
        userRoles.Any(r => r.Role == role && (r.OrgUnitId is null || units.Contains(r.OrgUnitId.Value)));
}
