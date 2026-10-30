namespace PBRMS.Domain;

/// <summary>
/// A permanent record that someone was booked outside their roster: called in on a day
/// off, or booked outside their shift. Off days are more of a suggestion while NBC has
/// more productions than operators, so this is allowed, but supervisors, the TV Operations
/// Manager and HR want to see how often it happens and to whom.
///
/// Written when someone acknowledges the warning. The rows stay even if the roster
/// changes later, because they record what was known and decided at the time.
/// </summary>
public class OffRosterWork
{
    public Guid Id { get; set; }

    /// <summary>The assignment, which gives the operator, the production and the hours.</summary>
    public Guid CrewAssignmentId { get; set; }
    public CrewAssignment CrewAssignment { get; set; } = null!;

    /// <summary><see cref="ClashKind.CallIn"/> or <see cref="ClashKind.OutsideShift"/>.</summary>
    public ClashKind Kind { get; set; }

    /// <summary>What the roster said at the time, e.g. "Day off on the roster (O: Off)".</summary>
    public string Reason { get; set; } = "";

    /// <summary>Who acknowledged the warning and went ahead.</summary>
    public Guid RecordedById { get; set; }
    public User RecordedBy { get; set; } = null!;

    public DateTimeOffset RecordedAt { get; set; }
}
