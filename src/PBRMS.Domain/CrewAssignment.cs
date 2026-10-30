namespace PBRMS.Domain;

/// <summary>
/// An operator put on a crew position by a supervisor, the Booking Officer, or the roster.
/// A slot has at most one current assignment. Changing the operator doesn't overwrite
/// the row: the old one gets <see cref="RemovedAt"/> and a new one is added, so we keep
/// who was on the crew and who changed it (Backend Schema Document, "Audit and deletion").
/// </summary>
/// <remarks>
/// Added with the features that need them: transport_needed (transport lists) and
/// stand_in_for (crew changes after confirmation, BR-13).
/// </remarks>
public class CrewAssignment
{
    public Guid Id { get; set; }

    public Guid CrewSlotId { get; set; }
    public CrewSlot CrewSlot { get; set; } = null!;

    public Guid OperatorId { get; set; }
    public User Operator { get; set; } = null!;

    /// <summary>Who decided. Empty when it was filled automatically from the monthly roster.</summary>
    public Guid? AssignedById { get; set; }
    public User? AssignedBy { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>When this operator was taken off the slot. Null while they are on it.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
}
