namespace PBRMS.Domain;

/// <summary>
/// A trip written down by hand: one from before PBRMS, or one nobody was crewed on here.
/// Without these the rotation would start everyone at zero and forget who has already
/// been out (SRS FR-TRP-01). Never deleted: a mistake gets <see cref="RemovedAt"/>.
/// </summary>
public class LoggedTrip
{
    public Guid Id { get; set; }

    public Guid OperatorId { get; set; }
    public User Operator { get; set; } = null!;

    public string Title { get; set; } = "";

    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    public Guid RecordedById { get; set; }
    public User RecordedBy { get; set; } = null!;

    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>When it was taken off as a mistake. Null while it counts.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
}
