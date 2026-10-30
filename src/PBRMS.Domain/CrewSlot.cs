namespace PBRMS.Domain;

/// <summary>
/// One crew position a booking needs, e.g. "Camera 2" from the Camera division.
/// When the EP approves, the booking goes to the supervisor of every division named
/// in its crew slots, and they fill the slots with operators (FR-BOOK-06).
/// </summary>
/// <remarks>
/// The schema document names a "discipline" here. Pointing at the TV Ops division
/// instead tells us which supervisor gets the booking, without a second list of
/// disciplines to keep in step with the divisions.
/// </remarks>
public class CrewSlot
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    /// <summary>The TV Ops division that supplies this position (Camera, Sound…).</summary>
    public Guid DivisionId { get; set; }
    public OrgUnit Division { get; set; } = null!;

    /// <summary>The position as it appears on the crew list, e.g. "Camera 2" or "Sound Supervisor".</summary>
    public string PositionLabel { get; set; } = "";

    /// <summary>When this position is needed. Usually the booking's own times, but a rigger may start earlier.</summary>
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Gear this position needs beyond the usual, e.g. "Steadicam".</summary>
    public string? SpecialEquipment { get; set; }
}
