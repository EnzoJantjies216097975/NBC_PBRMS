namespace PBRMS.Domain;

/// <summary>
/// One line of the audit log: who changed what, and when (BR-18). A line is written for every
/// saved change, in the same save, so a change can't happen without its line. Lines are never
/// changed or removed: no screen or endpoint can do either.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>Who made the change. Empty for PBRMS's own work, such as the minute checks or first-time setup.</summary>
    public Guid? ActorId { get; set; }
    public User? Actor { get; set; }

    /// <summary>The kind of thing that changed, in words: "Booking", "Role Assignment".</summary>
    public string What { get; set; } = "";

    /// <summary>The id of the thing that changed.</summary>
    public string ItemId { get; set; } = "";

    public AuditAction Action { get; set; }

    /// <summary>
    /// The values. For a change, each one that moved: "Status: Draft → EpReview". Ids stay ids
    /// here, and are turned into names when the log is read, so a rename can't rewrite history.
    /// </summary>
    public string Summary { get; set; } = "";
}

public enum AuditAction
{
    Added,
    Changed,
    Removed
}
