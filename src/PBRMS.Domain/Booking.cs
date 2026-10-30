namespace PBRMS.Domain;

/// <summary>
/// One production booking: the digital version of the carbon-copy booking form.
/// A producer fills it in, it goes through the approval chain
/// (<see cref="BookingWorkflow"/>), and it ends up as a confirmed, crewed production.
/// Bookings are never deleted; calling one off sets <see cref="BookingStatus.Cancelled"/>.
/// Source: Backend Schema Document, "bookings".
/// </summary>
/// <remarks>
/// Not here yet, and added with the features that need them: the link to the
/// production catalogue, planned minutes (overtime) and attachments (scripts, run-downs).
/// </remarks>
public class Booking
{
    public Guid Id { get; set; }

    /// <summary>The number people quote on the phone and on printouts. The database gives it out in order.</summary>
    public int ProductionNo { get; set; }

    public string Title { get; set; } = "";

    /// <summary>The content department the production is for. Its PM and EP approve it.</summary>
    public Guid DepartmentId { get; set; }
    public OrgUnit Department { get; set; } = null!;

    public Guid ProducerId { get; set; }
    public User Producer { get; set; } = null!;

    public ActivityType ActivityType { get; set; }
    public Venue Venue { get; set; }

    /// <summary>Where exactly, for venues that need it (e.g. the address of a location shoot).</summary>
    public string? VenueText { get; set; }

    /// <summary>When the crew must be there.</summary>
    public DateTimeOffset CallAt { get; set; }

    /// <summary>When the production starts and ends. A multi-day trip spans several days.</summary>
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>When it goes on air, if it does. Checked by the EP.</summary>
    public DateTimeOffset? AirAt { get; set; }

    /// <summary>The channel it airs on. Set by the EP.</summary>
    public Channel? Channel { get; set; }

    /// <summary>Out of town. Trips are shared out fairly between operators (BR-10).</summary>
    public bool IsTrip { get; set; }

    /// <summary>An outside broadcast.</summary>
    public bool IsOb { get; set; }

    /// <summary>The finance code the production's costs go to.</summary>
    public string? AllocationCode { get; set; }

    public string? Notes { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Draft;

    /// <summary>The crew the production needs, one entry per position (e.g. "Camera 2").</summary>
    public List<CrewSlot> CrewSlots { get; set; } = [];

    /// <summary>When the producer confirmed it started and ended (the booked times are <see cref="StartsAt"/> and <see cref="EndsAt"/>).</summary>
    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? ActualEndAt { get; set; }

    /// <summary>When people were told it had gone overtime, so they are told only once.</summary>
    public DateTimeOffset? OvertimeNotifiedAt { get; set; }

    /// <summary>Every status change, oldest first: who did it, when, and why (TR-03).</summary>
    public List<BookingStatusChange> History { get; set; } = [];

    /// <summary>
    /// Changed by SQL Server on every save. If two people act on the same booking at
    /// once, the second save fails instead of silently overwriting the first.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>What kind of work the booking is for (Backend Schema Document).</summary>
public enum ActivityType
{
    Recording,
    Transmission,
    Streaming,
    Kiloview,
    Rehearsal,
    Editing,
    Other
}

/// <summary>Where the production happens (Backend Schema Document).</summary>
public enum Venue
{
    Studio1,
    Studio2,
    Studio3,
    Studio4,
    /// <summary>Audio post-production.</summary>
    App,
    Location,
    /// <summary>Outside broadcast.</summary>
    Ob,
    FlyAway,
    Streaming
}

/// <summary>NBC's TV channels.</summary>
public enum Channel
{
    Nbc1,
    Nbc2,
    Nbc3
}
