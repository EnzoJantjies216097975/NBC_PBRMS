namespace PBRMS.Domain;

/// <summary>
/// A booking of an audio post-production (APP) operator. APP operators work directly with
/// producers and editors, so this skips the approval chain (BR-12): the producer or editor
/// asks, and the APP operator confirms based on the time.
/// If the operator isn't available then, perhaps because they're busy on a production, the
/// Booking Officer suggests a suitable time, and the producer or editor accepts it or cancels.
/// </summary>
public class AppSession
{
    public Guid Id { get; set; }

    /// <summary>The production the work is for.</summary>
    public string Production { get; set; } = "";

    /// <summary>The producer or editor who asked.</summary>
    public Guid RequesterId { get; set; }
    public User Requester { get; set; } = null!;

    /// <summary>The APP operator they asked for.</summary>
    public Guid OperatorId { get; set; }
    public User Operator { get; set; } = null!;

    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    public AppSessionStatus Status { get; set; } = AppSessionStatus.Requested;

    public string? Note { get; set; }

    /// <summary>The time the Booking Officer suggested. It replaces the times above once accepted.</summary>
    public DateTimeOffset? SuggestedStartsAt { get; set; }
    public DateTimeOffset? SuggestedEndsAt { get; set; }

    public Guid? SuggestedById { get; set; }
    public User? SuggestedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Changed by SQL Server on every save, so two people can't overwrite each other's decision.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Where an APP booking has got to.</summary>
public enum AppSessionStatus
{
    /// <summary>Waiting for the APP operator.</summary>
    Requested,

    /// <summary>The operator can't do that time. Waiting for the Booking Officer to suggest one.</summary>
    Unavailable,

    /// <summary>Waiting for the producer or editor to accept the suggested time.</summary>
    TimeSuggested,

    /// <summary>Agreed. The operator counts as busy at this time.</summary>
    Confirmed,

    /// <summary>Called off. Kept for the record.</summary>
    Cancelled
}
